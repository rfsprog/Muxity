using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;

namespace Muxity.Streaming.Services;

public record ResolvedStream(string VideoId, string HlsStoragePath);

/// <summary>
/// Validates a streaming key token and resolves it to the underlying video.
/// Results are cached in-memory for 60 seconds to reduce MongoDB round-trips
/// on hot paths (every segment request would otherwise hit the DB).
/// </summary>
public class StreamingKeyService
{
    private readonly MongoDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StreamingKeyService> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public StreamingKeyService(MongoDbContext db, IMemoryCache cache, ILogger<StreamingKeyService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    /// <summary>
    /// Validates <paramref name="key"/> and returns the resolved stream, or null if invalid/expired.
    /// </summary>
    public async Task<ResolvedStream?> ResolveAsync(string key, CancellationToken ct = default)
    {
        var cacheKey = $"sk:{key}";

        if (_cache.TryGetValue(cacheKey, out ResolvedStream? cached))
            return cached;

        var streamingKey = await _db.StreamingKeys
            .Find(k => k.Key == key)
            .FirstOrDefaultAsync(ct);

        if (streamingKey is null)
        {
            _logger.LogDebug("Streaming key not found: {Key}", key);
            return null;
        }

        if (streamingKey.ExpiresAt.HasValue && streamingKey.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogDebug("Streaming key expired: {Key}", key);
            return null;
        }

        var video = await _db.Videos
            .Find(v => v.Id == streamingKey.VideoId && v.Status == VideoStatus.Ready)
            .FirstOrDefaultAsync(ct);

        if (video is null)
        {
            _logger.LogDebug("Video not ready for key: {Key}", key);
            return null;
        }

        var resolved = new ResolvedStream(video.Id, video.HlsStoragePath ?? $"hls/{video.Id}");
        _cache.Set(cacheKey, resolved, CacheTtl);

        return resolved;
    }
}
