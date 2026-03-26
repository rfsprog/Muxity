using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Muxity.Streaming.Services.Cdn;

/// <summary>
/// Generates Cloudflare signed URLs using an HMAC-SHA256 signing secret.
/// Compatible with Cloudflare R2 presigned URLs and Cloudflare Stream token signing.
/// </summary>
public class CloudflareCdnProvider : ICdnProvider
{
    private readonly CloudflareSettings _settings;

    public CloudflareCdnProvider(IOptions<CdnSettings> options)
        => _settings = options.Value.Cloudflare;

    public string GetMasterPlaylistUrl(string videoId)
        => Sign(HlsPath(videoId, "master.m3u8"));

    public string GetQualityPlaylistUrl(string videoId, string quality)
        => Sign(HlsPath(videoId, $"{quality}/index.m3u8"));

    public string GetSegmentUrl(string videoId, string quality, string segment)
        => Sign(HlsPath(videoId, $"{quality}/{segment}"));

    // -------------------------------------------------------------------------

    private string HlsPath(string videoId, string file)
        => $"{_settings.BaseUrl.TrimEnd('/')}/hls/{videoId}/{file}";

    private string Sign(string url)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(_settings.ExpiryMinutes).ToUnixTimeSeconds();
        var urlWithExpiry = $"{url}?exp={expiry}";

        var keyBytes = Encoding.UTF8.GetBytes(_settings.SigningSecret);
        var msgBytes = Encoding.UTF8.GetBytes(urlWithExpiry);

        var sig = HMACSHA256.HashData(keyBytes, msgBytes);
        var sigHex = Convert.ToHexString(sig).ToLowerInvariant();

        return $"{urlWithExpiry}&sig={sigHex}";
    }
}
