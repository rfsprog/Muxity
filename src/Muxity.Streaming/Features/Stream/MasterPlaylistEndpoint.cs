using FastEndpoints;
using Muxity.Shared.Storage;
using Muxity.Streaming.Services;

namespace Muxity.Streaming.Features.Stream;

/// <summary>
/// GET /stream/{key}/master.m3u8
///
/// Validates the streaming key and either:
///   - CDN mode:        302 redirect to the CDN-signed master playlist URL
///   - Passthrough mode: stream master.m3u8 directly from storage
/// </summary>
public class MasterPlaylistEndpoint : EndpointWithoutRequest
{
    private readonly StreamingKeyService _keys;
    private readonly ICdnProvider _cdn;
    private readonly IStorageProvider _storage;

    public MasterPlaylistEndpoint(StreamingKeyService keys, ICdnProvider cdn, IStorageProvider storage)
    {
        _keys    = keys;
        _cdn     = cdn;
        _storage = storage;
    }

    public override void Configure()
    {
        Get("/stream/{key}/master.m3u8");
        AllowAnonymous();
        Summary(s => s.Summary = "Resolve a streaming key and serve or redirect to the HLS master playlist.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var key = Route<string>("key")!;

        var stream = await _keys.ResolveAsync(key, ct);
        if (stream is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var cdnUrl = _cdn.GetMasterPlaylistUrl(stream.VideoId);

        if (!string.IsNullOrEmpty(cdnUrl))
        {
            HttpContext.Response.Headers["Cache-Control"] = "no-store";
            await SendRedirectAsync(cdnUrl, isPermanent: false, allowRemoteRedirects: true);
            return;
        }

        // Passthrough: serve directly from storage
        var storagePath = $"{stream.HlsStoragePath}/master.m3u8";
        await ServeFromStorageAsync(storagePath, "application/x-mpegURL", ct);
    }

    private async Task ServeFromStorageAsync(string storagePath, string contentType, CancellationToken ct)
    {
        Stream fileStream;
        try
        {
            fileStream = await _storage.DownloadStreamAsync(storagePath, ct);
        }
        catch (FileNotFoundException)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        HttpContext.Response.ContentType = contentType;
        HttpContext.Response.Headers["Cache-Control"] = "no-cache";
        await using (fileStream)
            await fileStream.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
