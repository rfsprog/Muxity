using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muxity.Shared.Models;

public static class JobStatus
{
    public const string Queued     = "queued";
    public const string Claimed    = "claimed";
    public const string Processing = "processing";
    public const string Completed  = "completed";
    public const string Failed     = "failed";
}

public static class HardwareAccel
{
    public const string Auto     = "auto";
    public const string QSV      = "qsv";
    public const string NVENC    = "nvenc";
    public const string Software = "software";
}

public class TranscodeJob
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string VideoId { get; set; } = string.Empty;

    /// <summary>queued | claimed | processing | completed | failed</summary>
    public string Status { get; set; } = JobStatus.Queued;

    /// <summary>0–100 progress percentage, updated every ~5 seconds by the worker.</summary>
    public int Progress { get; set; }

    public string? WorkerNodeId { get; set; }

    /// <summary>auto | qsv | nvenc | software</summary>
    public string HardwareAccel { get; set; } = Models.HardwareAccel.Auto;

    public string? Error { get; set; }

    /// <summary>Number of times this job has been retried.</summary>
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
