using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muxity.Shared.Models;

public class WorkerNode
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Hostname { get; set; } = string.Empty;

    /// <summary>qsv | nvenc | software</summary>
    public string HardwareAccel { get; set; } = Models.HardwareAccel.Software;

    public int MaxParallelJobs { get; set; } = 2;

    public int ActiveJobs { get; set; }

    /// <summary>Updated by the worker heartbeat every 30 seconds.</summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
