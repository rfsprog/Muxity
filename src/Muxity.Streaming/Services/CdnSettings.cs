namespace Muxity.Streaming.Services;

public class CdnSettings
{
    /// <summary>Passthrough | CloudFront | Cloudflare</summary>
    public string Provider { get; set; } = "Passthrough";

    public CloudFrontSettings CloudFront { get; set; } = new();
    public CloudflareSettings Cloudflare { get; set; } = new();
}

public class CloudFrontSettings
{
    /// <summary>CloudFront distribution base URL, e.g. https://abc123.cloudfront.net</summary>
    public string BaseUrl        { get; set; } = string.Empty;
    public string KeyPairId      { get; set; } = string.Empty;
    /// <summary>Path to the PEM-encoded RSA private key file.</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;
    public int    ExpiryMinutes  { get; set; } = 60;
}

public class CloudflareSettings
{
    /// <summary>Cloudflare R2 / Stream public base URL.</summary>
    public string BaseUrl       { get; set; } = string.Empty;
    /// <summary>HMAC-SHA256 signing secret for Cloudflare URL signing.</summary>
    public string SigningSecret  { get; set; } = string.Empty;
    public int    ExpiryMinutes  { get; set; } = 60;
}
