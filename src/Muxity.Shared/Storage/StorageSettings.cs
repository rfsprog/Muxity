namespace Muxity.Shared.Storage;

public class StorageSettings
{
    /// <summary>Local | S3</summary>
    public string Provider { get; set; } = "Local";

    public LocalStorageSettings Local { get; set; } = new();
    public S3StorageSettings    S3    { get; set; } = new();
}

public class LocalStorageSettings
{
    /// <summary>Absolute path to the root directory where files are stored.</summary>
    public string BasePath { get; set; } = "/var/muxity/storage";

    /// <summary>Base URL the streaming API exposes for local file serving.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5200/files";
}

public class S3StorageSettings
{
    public string ServiceUrl      { get; set; } = string.Empty;
    public string BucketName      { get; set; } = "muxity";
    public string Region          { get; set; } = "us-east-1";
    public string AccessKeyId     { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Presigned URL expiry in minutes.</summary>
    public int PresignExpiryMinutes { get; set; } = 60;
}
