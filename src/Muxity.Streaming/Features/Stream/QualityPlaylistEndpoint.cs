using FastEndpoints;
using Muxity.Shared.Storage;
using Muxity.Streaming.Services;

namespace Muxity.Streaming.Features.Stream;

/// <summary>
/// GET /stream/{key}/{quality}/index.m3u8
///
/// Serves or redirects to the quality-level HLS playlist (e.g. 1080p/index.m3u8).
/// </summary>
public class QualityPlaylistEndpoint : EndpointWithoutRequest
{
    private readonly StreamingKeyService _keys;
    private readonly ICdnProvider _cdn;
    private readonly IStorageProvider _storage;

    public QualityPlaylistEndpoint(StreamingKeyService keys, ICdnProvider cdn, IStorageProvider storage)
    {
        _keys    = keys;
        _cdn     = cdn;
        _storage = storage;
    }

    public override void Configure()
    {
        Get("/stream/{key}/{quality}/index.m3u8");
        AllowAnonymous();
        Summary(s => s.Summary = "Serve or redirect to a quality-level HLS playlist.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var key     = Route<string>("key")!;
        var quality = Route<string>("quality")!;

        var stream = await _keys.ResolveAsync(key, ct);
        if (stream is null) { await SendNotFoundAsync(ct); return; }

        var cdnUrl = _cdn.GetQualityPlaylistUrl(stream.VideoId, quality);

        if (!string.IsNullOrEmpty(cdnUrl))
        {
            HttpContext.Response.Headers["Cache-Control"] = "no-store";
            await SendRedirectAsync(cdnUrl, isPermanent: false, allowRemoteRedirects: true);
            return;
        }

        var storagePath = $"{stream.HlsStoragePath}/{quality}/index.m3u8";
        await ServeFromStorageAsync(storagePath, "application/x-mpegURL", ct);
    }

    private async Task ServeFromStorageAsync(string storagePath, string contentType, CancellationToken ct)
    {
        Stream fileStream;
        try { fileStream = await _storage.DownloadStreamAsync(storagePath, ct); }
        catch (FileNotFoundException) { await SendNotFoundAsync(ct); return; }

        HttpContext.Response.ContentType = contentType;
        HttpContext.Response.Headers["Cache-Control"] = "public, max-age=3";
        await using (fileStream)
            await fileStream.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
