namespace Muxity.Streaming.Services.Cdn;

/// <summary>
/// Dev/local CDN provider. Returns internal streaming API paths that are
/// served directly from storage by <see cref="Features.Stream.SegmentEndpoint"/>
/// and <see cref="Features.Stream.PlaylistEndpoint"/>.
///
/// Not suitable for production at scale — use CloudFront or Cloudflare instead.
/// </summary>
public class PassthroughCdnProvider : ICdnProvider
{
    // These paths are handled by the streaming endpoints themselves,
    // so there is no redirect — callers stream directly.
    // Returning empty string signals "serve locally" to the endpoints.
    public string GetMasterPlaylistUrl(string videoId)             => string.Empty;
    public string GetQualityPlaylistUrl(string videoId, string quality) => string.Empty;
    public string GetSegmentUrl(string videoId, string quality, string segment) => string.Empty;
}
