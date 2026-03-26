using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class UpdateVideoRequest
{
    public string? Title       { get; set; }
    public string? Description { get; set; }

    /// <summary>public | private. Null = no change.</summary>
    public string? Visibility  { get; set; }
}

public class UpdateVideoEndpoint : Endpoint<UpdateVideoRequest, VideoDetailDto>
{
    private readonly MongoDbContext _db;

    public UpdateVideoEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Patch("/videos/{id}");
        Summary(s => s.Summary = "Update a video's title, description, or visibility.");
    }

    public override async Task HandleAsync(UpdateVideoRequest req, CancellationToken ct)
    {
        var id      = Route<string>("id")!;
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var video = await _db.Videos
            .Find(v => v.Id == id && v.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(ct);

        if (video is null) { await SendNotFoundAsync(ct); return; }
        if (video.OwnerId != ownerId) { await SendForbiddenAsync(ct); return; }

        var updates = new List<UpdateDefinition<Video>>();

        if (req.Title is not null)
            updates.Add(Builders<Video>.Update.Set(v => v.Title, req.Title.Trim()));

        if (req.Description is not null)
            updates.Add(Builders<Video>.Update.Set(v => v.Description, req.Description.Trim()));

        if (req.Visibility is VideoVisibility.Public or VideoVisibility.Private)
            updates.Add(Builders<Video>.Update.Set(v => v.Visibility, req.Visibility));

        if (updates.Count == 0)
        {
            AddError("", "No updatable fields provided.");
            await SendErrorsAsync(400, ct);
            return;
        }

        updates.Add(Builders<Video>.Update.Set(v => v.UpdatedAt, DateTime.UtcNow));

        var updated = await _db.Videos.FindOneAndUpdateAsync(
            Builders<Video>.Filter.Eq(v => v.Id, id),
            Builders<Video>.Update.Combine(updates),
            new FindOneAndUpdateOptions<Video> { ReturnDocument = ReturnDocument.After },
            ct);

        await SendOkAsync(new VideoDetailDto
        {
            Id              = updated.Id,
            OwnerId         = updated.OwnerId,
            Title           = updated.Title,
            Description     = updated.Description,
            Status          = updated.Status,
            Visibility      = updated.Visibility,
            ThumbnailPath   = updated.ThumbnailPath,
            DurationSeconds = updated.DurationSeconds,
            CreatedAt       = updated.CreatedAt,
            UpdatedAt       = updated.UpdatedAt,
        }, ct);
    }
}
