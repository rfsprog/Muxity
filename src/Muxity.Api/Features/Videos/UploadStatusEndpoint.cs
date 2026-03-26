using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class UploadStatusResponse
{
    public string  VideoId     { get; set; } = string.Empty;
    public string  Status      { get; set; } = string.Empty;
    public int?    Progress    { get; set; }
    public string? Error       { get; set; }
}

public class UploadStatusEndpoint : EndpointWithoutRequest<UploadStatusResponse>
{
    private readonly MongoDbContext _db;

    public UploadStatusEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/videos/{id}/upload-status");
        Summary(s => s.Summary = "Poll the upload and transcode status for a video.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id      = Route<string>("id")!;
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var video = await _db.Videos
            .Find(v => v.Id == id)
            .FirstOrDefaultAsync(ct);

        if (video is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Non-owners can only check status of public videos
        if (video.OwnerId != ownerId && video.Visibility != VideoVisibility.Public)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        // Fetch the latest job for progress
        var job = await _db.TranscodeJobs
            .Find(j => j.VideoId == id)
            .SortByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        await SendOkAsync(new UploadStatusResponse
        {
            VideoId  = video.Id,
            Status   = video.Status,
            Progress = job?.Progress,
            Error    = job?.Error,
        }, ct);
    }
}
