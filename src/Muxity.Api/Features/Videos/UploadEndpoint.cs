using FastEndpoints;
using MongoDB.Bson;
using Muxity.Api.Services.Messaging;
using Muxity.Shared.Data;
using Muxity.Shared.Messaging;
using Muxity.Shared.Models;
using Muxity.Shared.Storage;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Videos;

public class UploadRequest
{
    public string Title       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>public | private</summary>
    public string Visibility { get; set; } = VideoVisibility.Private;

    public IFormFile File { get; set; } = null!;
}

public class UploadResponse
{
    public string VideoId { get; set; } = string.Empty;
    public string Status  { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class UploadEndpoint : Endpoint<UploadRequest, UploadResponse>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "video/x-matroska", "video/quicktime", "video/webm",
        "video/x-msvideo", "video/mpeg",
    };

    private readonly MongoDbContext _db;
    private readonly IStorageProvider _storage;
    private readonly RabbitMqPublisher _mq;
    private readonly IConfiguration _config;

    public UploadEndpoint(
        MongoDbContext db,
        IStorageProvider storage,
        RabbitMqPublisher mq,
        IConfiguration config)
    {
        _db      = db;
        _storage = storage;
        _mq      = mq;
        _config  = config;
    }

    public override void Configure()
    {
        Post("/videos/upload");
        AllowFileUploads();
        Summary(s =>
        {
            s.Summary     = "Upload a raw video file for transcoding.";
            s.Description = "Multipart/form-data. Max file size is controlled by Storage:MaxFileSizeBytes (default 10 GB).";
        });
    }

    public override async Task HandleAsync(UploadRequest req, CancellationToken ct)
    {
        // ------------------------------------------------------------------
        // Auth
        // ------------------------------------------------------------------
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(ownerId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------
        if (req.File is null || req.File.Length == 0)
        {
            AddError("File", "A video file is required.");
            await SendErrorsAsync(400, ct);
            return;
        }

        var maxBytes = _config.GetValue<long>("Storage:MaxFileSizeBytes", 10L * 1024 * 1024 * 1024); // 10 GB
        if (req.File.Length > maxBytes)
        {
            AddError("File", $"File exceeds the maximum allowed size of {maxBytes / (1024 * 1024)} MB.");
            await SendErrorsAsync(413, ct);
            return;
        }

        if (!AllowedContentTypes.Contains(req.File.ContentType))
        {
            AddError("File", $"Unsupported file type '{req.File.ContentType}'. Allowed: mp4, mkv, mov, webm, avi, mpeg.");
            await SendErrorsAsync(415, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.Title))
        {
            AddError("Title", "Title is required.");
            await SendErrorsAsync(400, ct);
            return;
        }

        // ------------------------------------------------------------------
        // Create Video document
        // ------------------------------------------------------------------
        var videoId        = ObjectId.GenerateNewId().ToString();
        var rawStoragePath = $"raw/{videoId}/{req.File.FileName}";
        var hlsBasePath    = $"hls/{videoId}";

        var video = new Video
        {
            Id             = videoId,
            OwnerId        = ownerId,
            Title          = req.Title.Trim(),
            Description    = req.Description.Trim(),
            Visibility     = req.Visibility == VideoVisibility.Public
                                ? VideoVisibility.Public
                                : VideoVisibility.Private,
            Status         = VideoStatus.Pending,
            RawStoragePath = rawStoragePath,
            HlsStoragePath = hlsBasePath,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };

        await _db.Videos.InsertOneAsync(video, cancellationToken: ct);

        // ------------------------------------------------------------------
        // Stream file to storage (no buffering to memory)
        // ------------------------------------------------------------------
        await using (var stream = req.File.OpenReadStream())
        {
            await _storage.UploadAsync(rawStoragePath, stream, req.File.ContentType, ct);
        }

        // ------------------------------------------------------------------
        // Create TranscodeJob + publish to queue
        // ------------------------------------------------------------------
        var job = new TranscodeJob
        {
            VideoId   = videoId,
            Status    = JobStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };

        await _db.TranscodeJobs.InsertOneAsync(job, cancellationToken: ct);

        var message = new TranscodeJobMessage
        {
            JobId          = job.Id,
            VideoId        = videoId,
            RawStoragePath = rawStoragePath,
            OutputBasePath = hlsBasePath,
        };

        var queueName = _config.GetValue<string>("RabbitMQ:TranscodeQueue", "transcode_jobs")!;
        await _mq.PublishAsync(queueName, message, ct);

        await SendCreatedAtAsync<UploadStatusEndpoint>(
            new { id = videoId },
            new UploadResponse
            {
                VideoId = videoId,
                Status  = VideoStatus.Pending,
                Message = "Video uploaded successfully. Transcoding has been queued.",
            },
            cancellation: ct);
    }
}
