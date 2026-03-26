using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class VideoDetailDto : VideoSummaryDto
{
    public string OwnerId { get; set; } = string.Empty;
}

public class GetVideoEndpoint : EndpointWithoutRequest<VideoDetailDto>
{
    private readonly MongoDbContext _db;

    public GetVideoEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/videos/{id}");
        AllowAnonymous();
        Summary(s => s.Summary = "Get a single video. Public videos are accessible without auth.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id      = Route<string>("id")!;
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var video = await _db.Videos
            .Find(v => v.Id == id && v.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(ct);

        if (video is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (video.Visibility == VideoVisibility.Private && video.OwnerId != ownerId)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        await SendOkAsync(new VideoDetailDto
        {
            Id              = video.Id,
            OwnerId         = video.OwnerId,
            Title           = video.Title,
            Description     = video.Description,
            Status          = video.Status,
            Visibility      = video.Visibility,
            ThumbnailPath   = video.ThumbnailPath,
            DurationSeconds = video.DurationSeconds,
            CreatedAt       = video.CreatedAt,
            UpdatedAt       = video.UpdatedAt,
        }, ct);
    }
}
