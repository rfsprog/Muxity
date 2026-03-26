namespace Muxity.Transcoder.Models;

public record QualityPreset(
    string Label,
    int    Width,
    int    Height,
    string VideoBitrate,
    string AudioBitrate,
    int    BandwidthBps)
{
    public static readonly QualityPreset[] DefaultLadder =
    [
        new("1080p", 1920, 1080, "4500k", "192k", 4_692_000),
        new("720p",  1280, 720,  "2500k", "128k", 2_628_000),
        new("480p",  854,  480,  "1000k", "128k", 1_128_000),
        new("360p",  640,  360,  "500k",  "96k",  596_000),
    ];
}
