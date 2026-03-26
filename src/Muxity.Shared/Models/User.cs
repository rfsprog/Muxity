using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muxity.Shared.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>The 'sub' claim from the identity provider.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>google | microsoft</summary>
    public string Provider { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
