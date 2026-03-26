using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Muxity.Shared.Storage;

/// <summary>
/// Stores files in an S3-compatible bucket (AWS S3, MinIO, Cloudflare R2, etc.).
/// Presigned GET URLs are returned from <see cref="GetPublicUrlAsync"/>.
/// </summary>
public class S3StorageProvider : IStorageProvider, IAsyncDisposable
{
    private readonly IAmazonS3 _client;
    private readonly S3StorageSettings _settings;

    public S3StorageProvider(IOptions<StorageSettings> options)
    {
        _settings = options.Value.S3;

        var credentials = new BasicAWSCredentials(_settings.AccessKeyId, _settings.SecretAccessKey);
        var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(_settings.Region) };

        if (!string.IsNullOrWhiteSpace(_settings.ServiceUrl))
        {
            config.ServiceURL     = _settings.ServiceUrl;
            config.ForcePathStyle = true; // Required for MinIO / non-AWS endpoints
            config.RegionEndpoint = null;
        }

        _client = new AmazonS3Client(credentials, config);
    }

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName  = _settings.BucketName,
            Key         = NormalizeKey(path),
            InputStream = content,
            ContentType = contentType,
        };
        await _client.PutObjectAsync(request, ct);
    }

    public async Task<Stream> DownloadStreamAsync(string path, CancellationToken ct = default)
    {
        var response = await _client.GetObjectAsync(_settings.BucketName, NormalizeKey(path), ct);
        return response.ResponseStream;
    }

    public Task<string> GetPublicUrlAsync(string path, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.BucketName,
            Key        = NormalizeKey(path),
            Expires    = DateTime.UtcNow.AddMinutes(_settings.PresignExpiryMinutes),
            Verb       = HttpVerb.GET,
        };
        return Task.FromResult(_client.GetPreSignedURL(request));
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_settings.BucketName, NormalizeKey(path), ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_settings.BucketName, NormalizeKey(path), ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static string NormalizeKey(string path) => path.TrimStart('/');

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
