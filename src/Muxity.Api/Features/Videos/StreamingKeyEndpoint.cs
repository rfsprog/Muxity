using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class StreamingKeyResponse
{
    public string VideoId    { get; set; } = string.Empty;
    public string Key        { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

public class StreamingKeyEndpoint : EndpointWithoutRequest<StreamingKeyResponse>
{
    private readonly MongoDbContext _db;

    public StreamingKeyEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/videos/{id}/streaming-key");
        Summary(s => s.Summary = "Return the streaming key for a video. Creates one if it doesn't exist (owner only).");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var videoId = Route<string>("id")!;
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var video = await _db.Videos
            .Find(v => v.Id == videoId && v.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(ct);

        if (video is null) { await SendNotFoundAsync(ct); return; }
        if (video.OwnerId != ownerId) { await SendForbiddenAsync(ct); return; }

        if (video.Status != VideoStatus.Ready)
        {
            AddError("", $"Video is not ready for streaming (status: {video.Status}).");
            await SendErrorsAsync(409, ct);
            return;
        }

        // Return existing key or create one atomically
        var key = await _db.StreamingKeys
            .Find(k => k.VideoId == videoId)
            .FirstOrDefaultAsync(ct);

        if (key is null)
        {
            key = new StreamingKey { VideoId = videoId, Key = Guid.NewGuid().ToString("N") };
            await _db.StreamingKeys.InsertOneAsync(key, cancellationToken: ct);
        }

        await SendOkAsync(new StreamingKeyResponse
        {
            VideoId   = key.VideoId,
            Key       = key.Key,
            ExpiresAt = key.ExpiresAt,
        }, ct);
    }
}
