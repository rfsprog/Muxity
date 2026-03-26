using FastEndpoints;
using Muxity.Shared.Storage;
using Muxity.Streaming.Services;

namespace Muxity.Streaming.Features.Stream;

/// <summary>
/// GET /stream/{key}/{quality}/{segment}
///
/// Serves or redirects to an individual .ts segment.
/// Segments are immutable once written, so they carry a long Cache-Control.
/// </summary>
public class SegmentEndpoint : EndpointWithoutRequest
{
    private readonly StreamingKeyService _keys;
    private readonly ICdnProvider _cdn;
    private readonly IStorageProvider _storage;

    public SegmentEndpoint(StreamingKeyService keys, ICdnProvider cdn, IStorageProvider storage)
    {
        _keys    = keys;
        _cdn     = cdn;
        _storage = storage;
    }

    public override void Configure()
    {
        Get("/stream/{key}/{quality}/{segment}");
        AllowAnonymous();
        Summary(s => s.Summary = "Serve or redirect to an HLS transport stream segment (.ts).");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var key     = Route<string>("key")!;
        var quality = Route<string>("quality")!;
        var segment = Route<string>("segment")!;

        // Only allow .ts segment files through this endpoint
        if (!segment.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var stream = await _keys.ResolveAsync(key, ct);
        if (stream is null) { await SendNotFoundAsync(ct); return; }

        var cdnUrl = _cdn.GetSegmentUrl(stream.VideoId, quality, segment);

        if (!string.IsNullOrEmpty(cdnUrl))
        {
            // Segments are immutable — CDN redirect can be cached by the player
            HttpContext.Response.Headers["Cache-Control"] = "public, max-age=3600";
            await SendRedirectAsync(cdnUrl, isPermanent: false, allowRemoteRedirects: true);
            return;
        }

        // Passthrough: stream .ts directly from storage
        var storagePath = $"{stream.HlsStoragePath}/{quality}/{segment}";

        Stream fileStream;
        try { fileStream = await _storage.DownloadStreamAsync(storagePath, ct); }
        catch (FileNotFoundException) { await SendNotFoundAsync(ct); return; }

        HttpContext.Response.ContentType = "video/mp2t";
        HttpContext.Response.Headers["Cache-Control"] = "public, max-age=3600";
        await using (fileStream)
            await fileStream.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
