using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class ListVideosRequest
{
    public int    Page       { get; set; } = 1;
    public int    PageSize   { get; set; } = 20;

    /// <summary>Filter by status. Empty = all.</summary>
    public string? Status     { get; set; }

    /// <summary>Filter by visibility. Empty = all owned videos.</summary>
    public string? Visibility { get; set; }

    /// <summary>createdAt | updatedAt | title</summary>
    public string SortBy        { get; set; } = "createdAt";
    public bool   SortAscending { get; set; } = false;
}

public class VideoSummaryDto
{
    public string  Id              { get; set; } = string.Empty;
    public string  Title           { get; set; } = string.Empty;
    public string  Description     { get; set; } = string.Empty;
    public string  Status          { get; set; } = string.Empty;
    public string  Visibility      { get; set; } = string.Empty;
    public string? ThumbnailPath   { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime CreatedAt      { get; set; }
    public DateTime UpdatedAt      { get; set; }
}

public class PagedResponse<T>
{
    public List<T> Items      { get; set; } = [];
    public int     Page       { get; set; }
    public int     PageSize   { get; set; }
    public long    TotalCount { get; set; }
}

public class ListVideosEndpoint : Endpoint<ListVideosRequest, PagedResponse<VideoSummaryDto>>
{
    private readonly MongoDbContext _db;

    public ListVideosEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/videos");
        Summary(s => s.Summary = "List the authenticated user's videos (paged).");
    }

    public override async Task HandleAsync(ListVideosRequest req, CancellationToken ct)
    {
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(ownerId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        req.PageSize = Math.Clamp(req.PageSize, 1, 100);
        req.Page     = Math.Max(1, req.Page);

        var filter = Builders<Video>.Filter.And(
            Builders<Video>.Filter.Eq(v => v.OwnerId, ownerId),
            Builders<Video>.Filter.Ne(v => v.Status, VideoStatus.Deleted));

        if (!string.IsNullOrEmpty(req.Status))
            filter &= Builders<Video>.Filter.Eq(v => v.Status, req.Status);

        if (!string.IsNullOrEmpty(req.Visibility))
            filter &= Builders<Video>.Filter.Eq(v => v.Visibility, req.Visibility);

        var sort = req.SortBy switch
        {
            "updatedAt" => req.SortAscending
                ? Builders<Video>.Sort.Ascending(v => v.UpdatedAt)
                : Builders<Video>.Sort.Descending(v => v.UpdatedAt),
            "title" => req.SortAscending
                ? Builders<Video>.Sort.Ascending(v => v.Title)
                : Builders<Video>.Sort.Descending(v => v.Title),
            _ => req.SortAscending
                ? Builders<Video>.Sort.Ascending(v => v.CreatedAt)
                : Builders<Video>.Sort.Descending(v => v.CreatedAt),
        };

        var total = await _db.Videos.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _db.Videos
            .Find(filter)
            .Sort(sort)
            .Skip((req.Page - 1) * req.PageSize)
            .Limit(req.PageSize)
            .ToListAsync(ct);

        await SendOkAsync(new PagedResponse<VideoSummaryDto>
        {
            Items      = items.Select(ToDto).ToList(),
            Page       = req.Page,
            PageSize   = req.PageSize,
            TotalCount = total,
        }, ct);
    }

    private static VideoSummaryDto ToDto(Video v) => new()
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
    };
}
