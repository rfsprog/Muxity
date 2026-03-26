using Microsoft.Extensions.Options;
using Muxity.Shared.Storage;

namespace Muxity.Api.Services.Storage;

/// <summary>
/// Stores files on the local filesystem under a configurable base path.
/// Suitable for single-node dev/staging or NFS-mounted shared volumes in K8s.
/// </summary>
public class LocalStorageProvider : IStorageProvider
{
    private readonly LocalStorageSettings _settings;

    public LocalStorageProvider(IOptions<StorageSettings> options)
        => _settings = options.Value.Local;

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var fullPath = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> DownloadStreamAsync(string path, CancellationToken ct = default)
    {
        var fullPath = FullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Storage object not found: {path}", fullPath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<string> GetPublicUrlAsync(string path, CancellationToken ct = default)
    {
        var url = $"{_settings.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = FullPath(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(File.Exists(FullPath(path)));

    private string FullPath(string path)
        => Path.Combine(_settings.BasePath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
}
