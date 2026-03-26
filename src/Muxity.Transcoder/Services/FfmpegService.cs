using Microsoft.Extensions.Options;
using Muxity.Shared.Models;
using Muxity.Shared.Storage;
using Muxity.Transcoder.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Muxity.Transcoder.Services;

public class TranscoderSettings
{
    public string HardwareAccel          { get; set; } = "Auto";
    public int    MaxParallelJobs        { get; set; } = 2;
    public string FfmpegPath             { get; set; } = "ffmpeg";
    public string FfprobePath            { get; set; } = "ffprobe";
    public int    SegmentDurationSeconds { get; set; } = 10;
}

public record FfmpegProgress(int Percent, string TimeCode);

public class FfmpegService
{
    private readonly TranscoderSettings _settings;
    private readonly ILogger<FfmpegService> _logger;

    private string? _resolvedAccel; // Cached after first detection

    public FfmpegService(IOptions<TranscoderSettings> settings, ILogger<FfmpegService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    // -------------------------------------------------------------------------
    // Hardware acceleration detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Probes available hardware encoders and returns the best available accel mode.
    /// Result is cached after first call.
    /// </summary>
    public async Task<string> DetectAccelerationAsync(CancellationToken ct = default)
    {
        if (_resolvedAccel is not null)
            return _resolvedAccel;

        var configured = _settings.HardwareAccel;

        if (configured.Equals(HardwareAccel.Software, StringComparison.OrdinalIgnoreCase))
        {
            _resolvedAccel = HardwareAccel.Software;
            return _resolvedAccel;
        }

        if (configured.Equals(HardwareAccel.NVENC, StringComparison.OrdinalIgnoreCase) ||
            configured.Equals(HardwareAccel.Auto,  StringComparison.OrdinalIgnoreCase))
        {
            if (await EncoderExistsAsync("h264_nvenc", ct))
            {
                _logger.LogInformation("Hardware acceleration: NVIDIA NVENC");
                _resolvedAccel = HardwareAccel.NVENC;
                return _resolvedAccel;
            }
        }

        if (configured.Equals(HardwareAccel.QSV, StringComparison.OrdinalIgnoreCase) ||
            configured.Equals(HardwareAccel.Auto, StringComparison.OrdinalIgnoreCase))
        {
            if (await EncoderExistsAsync("h264_qsv", ct))
            {
                _logger.LogInformation("Hardware acceleration: Intel QSV");
                _resolvedAccel = HardwareAccel.QSV;
                return _resolvedAccel;
            }
        }

        _logger.LogInformation("Hardware acceleration: software (libx264)");
        _resolvedAccel = HardwareAccel.Software;
        return _resolvedAccel;
    }

    private async Task<bool> EncoderExistsAsync(string encoder, CancellationToken ct)
    {
        try
        {
            var result = await RunProcessAsync(_settings.FfmpegPath,
                $"-hide_banner -encoders", ct, timeoutMs: 5000);
            return result.Contains(encoder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Duration probe
    // -------------------------------------------------------------------------

    public async Task<double> GetDurationAsync(string inputPath, CancellationToken ct = default)
    {
        var output = await RunProcessAsync(
            _settings.FfprobePath,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
            ct);

        return double.TryParse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d
            : 0;
    }

    // -------------------------------------------------------------------------
    // Transcoding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Transcodes <paramref name="inputPath"/> to HLS for one quality preset.
    /// Writes .ts segments and a per-quality .m3u8 into <paramref name="outputDir"/>.
    /// Calls <paramref name="onProgress"/> with 0–100 as encoding proceeds.
    /// </summary>
    public async Task TranscodeQualityAsync(
        string inputPath,
        string outputDir,
        QualityPreset preset,
        double totalDurationSeconds,
        string accel,
        Func<FfmpegProgress, Task> onProgress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var playlistPath = Path.Combine(outputDir, "index.m3u8");
        var segmentPattern = Path.Combine(outputDir, "seg_%05d.ts");

        var args = BuildFfmpegArgs(inputPath, playlistPath, segmentPattern, preset, accel);

        _logger.LogDebug("FFmpeg [{Preset}]: {Args}", preset.Label, args);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = _settings.FfmpegPath,
                Arguments              = args,
                RedirectStandardError  = true,
                RedirectStandardOutput = false,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };

        var progressParser = new FfmpegProgressParser(totalDurationSeconds);
        var lastReportedPercent = -1;
        var lastReportTime = DateTime.UtcNow;

        process.Start();

        await foreach (var line in ReadLinesAsync(process.StandardError, ct))
        {
            var parsed = progressParser.Parse(line);
            if (parsed is null) continue;

            // Report at most once every 5 seconds or on 5% jumps
            var now = DateTime.UtcNow;
            if (parsed.Percent != lastReportedPercent &&
                (now - lastReportTime).TotalSeconds >= 5 || parsed.Percent == 100)
            {
                lastReportedPercent = parsed.Percent;
                lastReportTime = now;
                await onProgress(parsed);
            }
        }

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"FFmpeg exited with code {process.ExitCode} for preset {preset.Label}.");
    }

    // -------------------------------------------------------------------------
    // Thumbnail
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts a single JPEG frame at 10% of the video's duration.
    /// Saves it to <paramref name="outputPath"/>.
    /// </summary>
    public async Task ExtractThumbnailAsync(
        string inputPath,
        string outputPath,
        double durationSeconds,
        CancellationToken ct = default)
    {
        var seekSeconds = Math.Max(1, durationSeconds * 0.10);
        var seekStr = TimeSpan.FromSeconds(seekSeconds).ToString(@"hh\:mm\:ss\.fff");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var args = $"-y -ss {seekStr} -i \"{inputPath}\" -vframes 1 -q:v 2 \"{outputPath}\"";
        await RunProcessAsync(_settings.FfmpegPath, args, ct);
    }

    // -------------------------------------------------------------------------
    // Master playlist
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates the HLS master playlist referencing per-quality sub-playlists.
    /// </summary>
    public void WriteMasterPlaylist(string outputPath, IEnumerable<QualityPreset> presets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:3");

        foreach (var p in presets)
        {
            sb.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={p.BandwidthBps},RESOLUTION={p.Width}x{p.Height},NAME=\"{p.Label}\"");
            sb.AppendLine($"{p.Label}/index.m3u8");
        }

        File.WriteAllText(outputPath, sb.ToString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string BuildFfmpegArgs(
        string input,
        string playlist,
        string segmentPattern,
        QualityPreset preset,
        string accel)
    {
        var seg = _settings.SegmentDurationSeconds;

        return accel switch
        {
            HardwareAccel.NVENC =>
                $"-y -hwaccel cuda -hwaccel_output_format cuda -i \"{input}\" " +
                $"-vf scale_cuda={preset.Width}:{preset.Height} " +
                $"-c:v h264_nvenc -b:v {preset.VideoBitrate} -c:a aac -b:a {preset.AudioBitrate} " +
                $"-f hls -hls_time {seg} -hls_playlist_type vod " +
                $"-hls_segment_filename \"{segmentPattern}\" \"{playlist}\"",

            HardwareAccel.QSV =>
                $"-y -hwaccel qsv -i \"{input}\" " +
                $"-vf scale_qsv={preset.Width}:{preset.Height} " +
                $"-c:v h264_qsv -b:v {preset.VideoBitrate} -c:a aac -b:a {preset.AudioBitrate} " +
                $"-f hls -hls_time {seg} -hls_playlist_type vod " +
                $"-hls_segment_filename \"{segmentPattern}\" \"{playlist}\"",

            _ =>
                $"-y -i \"{input}\" " +
                $"-vf scale={preset.Width}:{preset.Height} " +
                $"-c:v libx264 -b:v {preset.VideoBitrate} -c:a aac -b:a {preset.AudioBitrate} " +
                $"-f hls -hls_time {seg} -hls_playlist_type vod " +
                $"-hls_segment_filename \"{segmentPattern}\" \"{playlist}\"",
        };
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
            yield return line;
    }

    private async Task<string> RunProcessAsync(string exe, string args, CancellationToken ct, int timeoutMs = 30_000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };

        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        return stdout;
    }
}

/// <summary>Parses FFmpeg's stderr time= progress lines into a 0–100 percentage.</summary>
internal class FfmpegProgressParser
{
    private static readonly Regex TimeRegex =
        new(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);

    private readonly double _totalSeconds;

    public FfmpegProgressParser(double totalSeconds) => _totalSeconds = totalSeconds;

    public FfmpegProgress? Parse(string line)
    {
        var match = TimeRegex.Match(line);
        if (!match.Success) return null;

        var elapsed = int.Parse(match.Groups[1].Value) * 3600
                    + int.Parse(match.Groups[2].Value) * 60
                    + int.Parse(match.Groups[3].Value)
                    + int.Parse(match.Groups[4].Value) / 100.0;

        var pct = _totalSeconds > 0
            ? (int)Math.Min(100, elapsed / _totalSeconds * 100)
            : 0;

        return new FfmpegProgress(pct, match.Value.Replace("time=", ""));
    }
}
