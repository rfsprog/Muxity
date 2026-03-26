using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using Muxity.Shared.Storage;
using Muxity.Transcoder.Models;
using Muxity.Transcoder.Services;

namespace Muxity.Transcoder.Workers;

/// <summary>
/// Orchestrates the full transcoding pipeline:
///   1. Consume TranscodeJobMessage from RabbitMQ
///   2. Atomically claim the job in MongoDB
///   3. Download the raw file to a temp directory
///   4. Detect hardware acceleration (once, cached)
///   5. Transcode each quality preset with FFmpeg, reporting progress
///   6. Generate master HLS playlist
///   7. Extract thumbnail
///   8. Upload all output to storage
///   9. Update Video.Status = Ready and clean up temp files
/// </summary>
public class TranscoderWorker : BackgroundService
{
    private readonly JobConsumer _consumer;
    private readonly FfmpegService _ffmpeg;
    private readonly WorkerRegistrationService _registration;
    private readonly MongoDbContext _db;
    private readonly IStorageProvider _storage;
    private readonly TranscoderSettings _settings;
    private readonly ILogger<TranscoderWorker> _logger;

    // Controls max parallel jobs on this node
    private readonly SemaphoreSlim _slots;

    public TranscoderWorker(
        JobConsumer consumer,
        FfmpegService ffmpeg,
        WorkerRegistrationService registration,
        MongoDbContext db,
        IStorageProvider storage,
        IOptions<TranscoderSettings> settings,
        ILogger<TranscoderWorker> logger)
    {
        _consumer     = consumer;
        _ffmpeg       = ffmpeg;
        _registration = registration;
        _db           = db;
        _storage      = storage;
        _settings     = settings.Value;
        _logger       = logger;
        _slots        = new SemaphoreSlim(_settings.MaxParallelJobs, _settings.MaxParallelJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TranscoderWorker started (maxParallel={Max})", _settings.MaxParallelJobs);

        await foreach (var (message, deliveryTag) in _consumer.Reader.ReadAllAsync(stoppingToken))
        {
            // Acquire a processing slot (blocks if at capacity)
            await _slots.WaitAsync(stoppingToken);

            // Fire-and-forget with its own error boundary so one failure doesn't
            // kill the consumer loop
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessJobAsync(message, stoppingToken);
                    await _consumer.AckAsync(deliveryTag, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId} failed — sending to DLQ", message.JobId);
                    await MarkJobFailedAsync(message.JobId, ex.Message, stoppingToken);
                    await _consumer.NackAsync(deliveryTag, stoppingToken);
                }
                finally
                {
                    _slots.Release();
                }
            }, stoppingToken);
        }
    }

    // -------------------------------------------------------------------------

    private async Task ProcessJobAsync(Muxity.Shared.Messaging.TranscodeJobMessage message, CancellationToken ct)
    {
        var jobId   = message.JobId;
        var videoId = message.VideoId;

        // ------------------------------------------------------------------
        // 1. Atomically claim the job
        // ------------------------------------------------------------------
        var job = await _db.TranscodeJobs.FindOneAndUpdateAsync(
            Builders<TranscodeJob>.Filter.And(
                Builders<TranscodeJob>.Filter.Eq(j => j.Id, jobId),
                Builders<TranscodeJob>.Filter.Eq(j => j.Status, JobStatus.Queued)),
            Builders<TranscodeJob>.Update
                .Set(j => j.Status,       JobStatus.Claimed)
                .Set(j => j.WorkerNodeId, _registration.NodeId)
                .Set(j => j.StartedAt,    DateTime.UtcNow),
            new FindOneAndUpdateOptions<TranscodeJob> { ReturnDocument = ReturnDocument.After },
            ct);

        if (job is null)
        {
            _logger.LogWarning("Job {JobId} could not be claimed (already taken or missing).", jobId);
            return;
        }

        _logger.LogInformation("Claimed job {JobId} for video {VideoId}", jobId, videoId);

        await UpdateJobStatusAsync(jobId, JobStatus.Processing, progress: 0, ct: ct);
        await UpdateVideoStatusAsync(videoId, VideoStatus.Transcoding, ct);

        // ------------------------------------------------------------------
        // 2. Download raw file to temp dir
        // ------------------------------------------------------------------
        var tempDir  = Path.Combine(Path.GetTempPath(), "muxity", jobId);
        var hlsTempDir = Path.Combine(tempDir, "hls");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(hlsTempDir);

        string rawLocalPath;
        try
        {
            rawLocalPath = await DownloadRawAsync(message.RawStoragePath, tempDir, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to download raw file: {ex.Message}", ex);
        }

        try
        {
            // ----------------------------------------------------------------
            // 3. Probe duration
            // ----------------------------------------------------------------
            var duration = await _ffmpeg.GetDurationAsync(rawLocalPath, ct);
            _logger.LogInformation("Video {VideoId} duration: {Duration:F1}s", videoId, duration);

            // ----------------------------------------------------------------
            // 4. Detect hardware acceleration
            // ----------------------------------------------------------------
            var accel = await _ffmpeg.DetectAccelerationAsync(ct);

            // ----------------------------------------------------------------
            // 5. Transcode each quality preset
            // ----------------------------------------------------------------
            var presets = QualityPreset.DefaultLadder;
            var totalPresets = presets.Length;

            for (var i = 0; i < totalPresets; i++)
            {
                var preset    = presets[i];
                var presetDir = Path.Combine(hlsTempDir, preset.Label);
                var presetBase = i * (100 / totalPresets);
                var presetRange = 100 / totalPresets;

                _logger.LogInformation("Transcoding {VideoId} → {Preset} ({Accel})", videoId, preset.Label, accel);

                await _ffmpeg.TranscodeQualityAsync(
                    inputPath:             rawLocalPath,
                    outputDir:             presetDir,
                    preset:                preset,
                    totalDurationSeconds:  duration,
                    accel:                 accel,
                    onProgress: async p =>
                    {
                        var overall = presetBase + (int)(p.Percent * presetRange / 100.0);
                        await UpdateJobStatusAsync(jobId, JobStatus.Processing, progress: overall, ct: ct);
                    },
                    ct: ct);
            }

            // ----------------------------------------------------------------
            // 6. Write master playlist
            // ----------------------------------------------------------------
            var masterPath = Path.Combine(hlsTempDir, "master.m3u8");
            _ffmpeg.WriteMasterPlaylist(masterPath, presets);

            // ----------------------------------------------------------------
            // 7. Extract thumbnail
            // ----------------------------------------------------------------
            var thumbLocalPath = Path.Combine(tempDir, "thumb.jpg");
            await _ffmpeg.ExtractThumbnailAsync(rawLocalPath, thumbLocalPath, duration, ct);

            // ----------------------------------------------------------------
            // 8. Upload HLS segments + master to storage
            // ----------------------------------------------------------------
            await UploadDirectoryAsync(hlsTempDir, message.OutputBasePath, ct);

            // Upload thumbnail
            var thumbStoragePath = $"thumbs/{videoId}/thumb.jpg";
            await using (var thumbStream = File.OpenRead(thumbLocalPath))
                await _storage.UploadAsync(thumbStoragePath, thumbStream, "image/jpeg", ct);

            // ----------------------------------------------------------------
            // 9. Mark complete
            // ----------------------------------------------------------------
            await _db.Videos.UpdateOneAsync(
                Builders<Video>.Filter.Eq(v => v.Id, videoId),
                Builders<Video>.Update
                    .Set(v => v.Status,         VideoStatus.Ready)
                    .Set(v => v.ThumbnailPath,  thumbStoragePath)
                    .Set(v => v.DurationSeconds, duration)
                    .Set(v => v.UpdatedAt,       DateTime.UtcNow),
                cancellationToken: ct);

            await UpdateJobStatusAsync(jobId, JobStatus.Completed, progress: 100, ct: ct);

            // Auto-create streaming key now that video is ready
            await CreateStreamingKeyAsync(videoId, ct);

            _logger.LogInformation("Job {JobId} completed successfully for video {VideoId}", jobId, videoId);
        }
        finally
        {
            // Always clean up temp files
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp dir {Dir}", tempDir); }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> DownloadRawAsync(string storagePath, string tempDir, CancellationToken ct)
    {
        var fileName  = Path.GetFileName(storagePath);
        var localPath = Path.Combine(tempDir, fileName);

        await using var srcStream  = await _storage.DownloadStreamAsync(storagePath, ct);
        await using var destStream = new FileStream(localPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 81920, useAsync: true);
        await srcStream.CopyToAsync(destStream, ct);

        return localPath;
    }

    private async Task UploadDirectoryAsync(string localDir, string storageBasePath, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories))
        {
            var relative     = Path.GetRelativePath(localDir, file).Replace('\\', '/');
            var storagePath  = $"{storageBasePath.TrimEnd('/')}/{relative}";
            var contentType  = file.EndsWith(".m3u8") ? "application/x-mpegURL" : "video/mp2t";

            await using var stream = File.OpenRead(file);
            await _storage.UploadAsync(storagePath, stream, contentType, ct);
        }
    }

    private async Task UpdateJobStatusAsync(
        string jobId, string status, int progress, CancellationToken ct)
    {
        await _db.TranscodeJobs.UpdateOneAsync(
            Builders<TranscodeJob>.Filter.Eq(j => j.Id, jobId),
            Builders<TranscodeJob>.Update
                .Set(j => j.Status,   status)
                .Set(j => j.Progress, progress),
            cancellationToken: ct);
    }

    private async Task UpdateVideoStatusAsync(string videoId, string status, CancellationToken ct)
    {
        await _db.Videos.UpdateOneAsync(
            Builders<Video>.Filter.Eq(v => v.Id, videoId),
            Builders<Video>.Update
                .Set(v => v.Status,    status)
                .Set(v => v.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    private async Task MarkJobFailedAsync(string jobId, string error, CancellationToken ct)
    {
        var job = await _db.TranscodeJobs
            .Find(j => j.Id == jobId)
            .FirstOrDefaultAsync(ct);

        if (job is null) return;

        await _db.TranscodeJobs.UpdateOneAsync(
            Builders<TranscodeJob>.Filter.Eq(j => j.Id, jobId),
            Builders<TranscodeJob>.Update
                .Set(j => j.Status,      JobStatus.Failed)
                .Set(j => j.Error,       error)
                .Set(j => j.CompletedAt, DateTime.UtcNow),
            cancellationToken: ct);

        await UpdateVideoStatusAsync(job.VideoId, VideoStatus.Failed, ct);
    }

    private async Task CreateStreamingKeyAsync(string videoId, CancellationToken ct)
    {
        var existing = await _db.StreamingKeys
            .Find(k => k.VideoId == videoId)
            .FirstOrDefaultAsync(ct);

        if (existing is not null) return;

        await _db.StreamingKeys.InsertOneAsync(new StreamingKey
        {
            VideoId = videoId,
            Key     = Guid.NewGuid().ToString("N"),
        }, cancellationToken: ct);
    }
}
