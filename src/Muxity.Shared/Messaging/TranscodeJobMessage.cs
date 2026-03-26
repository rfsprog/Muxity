namespace Muxity.Shared.Messaging;

/// <summary>
/// Published to RabbitMQ after a raw video file has been stored.
/// Consumed by Muxity.Transcoder worker nodes.
/// </summary>
public class TranscodeJobMessage
{
    public string JobId          { get; set; } = string.Empty;
    public string VideoId        { get; set; } = string.Empty;
    public string RawStoragePath { get; set; } = string.Empty;
    public string OutputBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Quality presets to generate. Null = use the worker's default ladder.
    /// Each entry is a label like "1080p", "720p", "480p", "360p".
    /// </summary>
    public string[]? RequestedQualities { get; set; }
}
