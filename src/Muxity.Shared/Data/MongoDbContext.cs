using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Muxity.Shared.Constants;
using Muxity.Shared.Models;

namespace Muxity.Shared.Data;

public class MongoDbContext
{
    private readonly IMongoDatabase _db;

    public MongoDbContext(IOptions<MongoDbSettings> options)
    {
        var settings = options.Value;
        var client = new MongoClient(settings.ConnectionString);
        _db = client.GetDatabase(settings.DatabaseName);
    }

    public IMongoCollection<User>         Users         => _db.GetCollection<User>(Collections.Users);
    public IMongoCollection<Video>        Videos        => _db.GetCollection<Video>(Collections.Videos);
    public IMongoCollection<TranscodeJob> TranscodeJobs => _db.GetCollection<TranscodeJob>(Collections.TranscodeJobs);
    public IMongoCollection<StreamingKey> StreamingKeys => _db.GetCollection<StreamingKey>(Collections.StreamingKeys);
    public IMongoCollection<RefreshToken> RefreshTokens => _db.GetCollection<RefreshToken>(Collections.RefreshTokens);
    public IMongoCollection<WorkerNode>   WorkerNodes   => _db.GetCollection<WorkerNode>(Collections.WorkerNodes);

    /// <summary>
    /// Creates all required indexes. Safe to call on every startup (idempotent).
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // Users: unique compound index on provider + externalId
        await Users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
            Builders<User>.IndexKeys
                .Ascending(u => u.Provider)
                .Ascending(u => u.ExternalId),
            new CreateIndexOptions { Unique = true, Name = "idx_users_provider_externalId" }));

        // Videos: list by owner
        await Videos.Indexes.CreateOneAsync(new CreateIndexModel<Video>(
            Builders<Video>.IndexKeys.Ascending(v => v.OwnerId),
            new CreateIndexOptions { Name = "idx_videos_ownerId" }));

        // Videos: filter by status
        await Videos.Indexes.CreateOneAsync(new CreateIndexModel<Video>(
            Builders<Video>.IndexKeys.Ascending(v => v.Status),
            new CreateIndexOptions { Name = "idx_videos_status" }));

        // Videos: full-text search on title + description
        await Videos.Indexes.CreateOneAsync(new CreateIndexModel<Video>(
            Builders<Video>.IndexKeys
                .Text(v => v.Title)
                .Text(v => v.Description),
            new CreateIndexOptions { Name = "idx_videos_text" }));

        // TranscodeJobs: lookup by video
        await TranscodeJobs.Indexes.CreateOneAsync(new CreateIndexModel<TranscodeJob>(
            Builders<TranscodeJob>.IndexKeys.Ascending(j => j.VideoId),
            new CreateIndexOptions { Name = "idx_jobs_videoId" }));

        // TranscodeJobs: worker polling (queued jobs)
        await TranscodeJobs.Indexes.CreateOneAsync(new CreateIndexModel<TranscodeJob>(
            Builders<TranscodeJob>.IndexKeys.Ascending(j => j.Status),
            new CreateIndexOptions { Name = "idx_jobs_status" }));

        // StreamingKeys: fast lookup by key token
        await StreamingKeys.Indexes.CreateOneAsync(new CreateIndexModel<StreamingKey>(
            Builders<StreamingKey>.IndexKeys.Ascending(k => k.Key),
            new CreateIndexOptions { Unique = true, Name = "idx_streamingkeys_key" }));

        // StreamingKeys: lookup by video
        await StreamingKeys.Indexes.CreateOneAsync(new CreateIndexModel<StreamingKey>(
            Builders<StreamingKey>.IndexKeys.Ascending(k => k.VideoId),
            new CreateIndexOptions { Name = "idx_streamingkeys_videoId" }));

        // RefreshTokens: fast lookup by token value
        await RefreshTokens.Indexes.CreateOneAsync(new CreateIndexModel<RefreshToken>(
            Builders<RefreshToken>.IndexKeys.Ascending(r => r.Token),
            new CreateIndexOptions { Unique = true, Name = "idx_refreshtokens_token" }));

        // RefreshTokens: TTL — auto-expire documents after ExpiresAt
        await RefreshTokens.Indexes.CreateOneAsync(new CreateIndexModel<RefreshToken>(
            Builders<RefreshToken>.IndexKeys.Ascending(r => r.ExpiresAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "idx_refreshtokens_ttl" }));
    }
}
