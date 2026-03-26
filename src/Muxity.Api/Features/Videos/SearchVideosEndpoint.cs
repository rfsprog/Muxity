using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;

namespace Muxity.Api.Features.Videos;

public class SearchVideosRequest
{
    public string Q        { get; set; } = string.Empty;
    public int    Page     { get; set; } = 1;
    public int    PageSize { get; set; } = 20;
}

public class SearchVideosEndpoint : Endpoint<SearchVideosRequest, PagedResponse<VideoSummaryDto>>
{
    private readonly MongoDbContext _db;

    public SearchVideosEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/videos/search");
        AllowAnonymous();
        Summary(s => s.Summary = "Full-text search across public video titles and descriptions.");
    }

    public override async Task HandleAsync(SearchVideosRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Q))
        {
            AddError("q", "Search query is required.");
            await SendErrorsAsync(400, ct);
            return;
        }

        req.PageSize = Math.Clamp(req.PageSize, 1, 100);
        req.Page     = Math.Max(1, req.Page);

        var filter = Builders<Video>.Filter.And(
            Builders<Video>.Filter.Text(req.Q),
            Builders<Video>.Filter.Eq(v => v.Visibility, VideoVisibility.Public),
            Builders<Video>.Filter.Eq(v => v.Status, VideoStatus.Ready));

        var sort  = Builders<Video>.Sort.MetaTextScore("score");
        var proj  = Builders<Video>.Projection.MetaTextScore("score");

        var total = await _db.Videos.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _db.Videos
            .Find(filter)
            .Sort(sort)
            .Skip((req.Page - 1) * req.PageSize)
            .Limit(req.PageSize)
            .ToListAsync(ct);

        await SendOkAsync(new PagedResponse<VideoSummaryDto>
        {
            Items = items.Select(v => new VideoSummaryDto
            {
                Id              = v.Id,
                Title           = v.Title,
                Description     = v.Description,
                Status          = v.Status,
                Visibility      = v.Visibility,
                ThumbnailPath   = v.ThumbnailPath,
                DurationSeconds = v.DurationSeconds,
                CreatedAt       = v.CreatedAt,
                UpdatedAt       = v.UpdatedAt,
            }).ToList(),
            Page       = req.Page,
            PageSize   = req.PageSize,
            TotalCount = total,
        }, ct);
    }
}
