namespace Muxity.Shared.Storage;

/// <summary>
/// Abstraction over the underlying binary store (local filesystem or S3-compatible).
/// All paths use forward slashes and are relative to the storage root.
/// </summary>
public interface IStorageProvider
{
    /// <summary>Upload a stream to <paramref name="path"/>. Creates intermediate directories as needed.</summary>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Open a readable stream for the object at <paramref name="path"/>.</summary>
    Task<Stream> DownloadStreamAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Returns a URL suitable for CDN/HTTP delivery.
    /// For local storage this is a relative API path; for S3 it is a presigned URL.
    /// </summary>
    Task<string> GetPublicUrlAsync(string path, CancellationToken ct = default);

    /// <summary>Delete the object at <paramref name="path"/>. No-op if it does not exist.</summary>
    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Returns true if an object exists at <paramref name="path"/>.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
}
