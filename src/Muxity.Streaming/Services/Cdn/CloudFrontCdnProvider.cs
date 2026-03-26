using Amazon.CloudFront;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Muxity.Streaming.Services.Cdn;

/// <summary>
/// Generates CloudFront signed URLs using a trusted key pair.
/// Requires an RSA private key file (PEM) and a CloudFront Key Pair ID.
/// </summary>
public class CloudFrontCdnProvider : ICdnProvider
{
    private readonly CloudFrontSettings _settings;
    private readonly RSA _rsa;

    public CloudFrontCdnProvider(IOptions<CdnSettings> options)
    {
        _settings = options.Value.CloudFront;

        _rsa = RSA.Create();
        var pem = File.ReadAllText(_settings.PrivateKeyPath);
        _rsa.ImportFromPem(pem);
    }

    public string GetMasterPlaylistUrl(string videoId)
        => Sign(HlsPath(videoId, "master.m3u8"));

    public string GetQualityPlaylistUrl(string videoId, string quality)
        => Sign(HlsPath(videoId, $"{quality}/index.m3u8"));

    public string GetSegmentUrl(string videoId, string quality, string segment)
        => Sign(HlsPath(videoId, $"{quality}/{segment}"));

    // -------------------------------------------------------------------------

    private string HlsPath(string videoId, string file)
        => $"{_settings.BaseUrl.TrimEnd('/')}/hls/{videoId}/{file}";

    private string Sign(string resourceUrl)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(_settings.ExpiryMinutes).ToUnixTimeSeconds();

        return AmazonCloudFrontUrlSigner.GetCannedSignedURL(
            resourceUrl,
            _rsa,
            _settings.KeyPairId,
            DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes));
    }
}
