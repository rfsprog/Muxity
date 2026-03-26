namespace Muxity.Streaming.Services;

/// <summary>
/// Resolves delivery URLs for HLS content.
/// Implementations: Passthrough (dev), CloudFront, Cloudflare.
/// </summary>
public interface ICdnProvider
{
    /// <summary>URL for the master playlist of a video.</summary>
    string GetMasterPlaylistUrl(string videoId);

    /// <summary>URL for a quality-level playlist (e.g. 1080p/index.m3u8).</summary>
    string GetQualityPlaylistUrl(string videoId, string quality);

    /// <summary>URL for an individual transport stream segment.</summary>
    string GetSegmentUrl(string videoId, string quality, string segment);
}
