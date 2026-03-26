using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class DeleteVideoEndpoint : EndpointWithoutRequest
{
    private readonly MongoDbContext _db;

    public DeleteVideoEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Delete("/videos/{id}");
        Summary(s => s.Summary = "Soft-delete a video. Removes its streaming key and marks it as deleted.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id      = Route<string>("id")!;
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var video = await _db.Videos
            .Find(v => v.Id == id && v.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(ct);

        if (video is null) { await SendNotFoundAsync(ct); return; }
        if (video.OwnerId != ownerId) { await SendForbiddenAsync(ct); return; }

        // Soft-delete the video
        await _db.Videos.UpdateOneAsync(
            Builders<Video>.Filter.Eq(v => v.Id, id),
            Builders<Video>.Update
                .Set(v => v.Status, VideoStatus.Deleted)
                .Set(v => v.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);

        // Remove associated streaming keys
        await _db.StreamingKeys.DeleteManyAsync(
            Builders<StreamingKey>.Filter.Eq(k => k.VideoId, id),
            ct);

        // Storage cleanup is deferred to a background job in Phase 3
        await SendNoContentAsync(ct);
    }
}
