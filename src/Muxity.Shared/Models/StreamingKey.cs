using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muxity.Shared.Models;

public class StreamingKey
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string VideoId { get; set; } = string.Empty;

    /// <summary>Opaque token used in streaming URLs. Generated as a GUID.</summary>
    public string Key { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Null = permanent key. Set to expire signed/temporary keys.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
