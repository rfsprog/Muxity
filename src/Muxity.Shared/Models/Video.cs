using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muxity.Shared.Models;

public static class VideoStatus
{
    public const string Pending     = "pending";
    public const string Transcoding = "transcoding";
    public const string Ready       = "ready";
    public const string Failed      = "failed";
    public const string Deleted     = "deleted";
}

public static class VideoVisibility
{
    public const string Public  = "public";
    public const string Private = "private";
}

public class Video
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string OwnerId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>pending | transcoding | ready | failed | deleted</summary>
    public string Status { get; set; } = VideoStatus.Pending;

    /// <summary>public | private</summary>
    public string Visibility { get; set; } = VideoVisibility.Private;

    /// <summary>Path to the raw uploaded file in storage.</summary>
    public string? RawStoragePath { get; set; }

    /// <summary>Base path for HLS output segments in storage.</summary>
    public string? HlsStoragePath { get; set; }

    /// <summary>Path to generated thumbnail in storage.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Populated after successful transcoding.</summary>
    public double? DurationSeconds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
