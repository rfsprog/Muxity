using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;

namespace Muxity.Transcoder.Services;

/// <summary>
/// Registers this worker node in MongoDB on startup and sends a heartbeat
/// every 30 seconds so the cluster can detect stale nodes.
/// </summary>
public class WorkerRegistrationService : IHostedService, IAsyncDisposable
{
    private readonly MongoDbContext _db;
    private readonly FfmpegService _ffmpeg;
    private readonly TranscoderSettings _settings;
    private readonly ILogger<WorkerRegistrationService> _logger;

    private string? _nodeId;
    private Timer?  _heartbeatTimer;

    public string NodeId => _nodeId ?? throw new InvalidOperationException("Not yet registered.");

    public WorkerRegistrationService(
        MongoDbContext db,
        FfmpegService ffmpeg,
        IOptions<TranscoderSettings> settings,
        ILogger<WorkerRegistrationService> logger)
    {
        _db       = db;
        _ffmpeg   = ffmpeg;
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var accel    = await _ffmpeg.DetectAccelerationAsync(ct);
        var hostname = Environment.MachineName;

        var node = new WorkerNode
        {
            Hostname       = hostname,
            HardwareAccel  = accel,
            MaxParallelJobs = _settings.MaxParallelJobs,
            RegisteredAt   = DateTime.UtcNow,
            LastHeartbeat  = DateTime.UtcNow,
        };

        await _db.WorkerNodes.InsertOneAsync(node, cancellationToken: ct);
        _nodeId = node.Id;

        _logger.LogInformation(
            "Worker node registered: {NodeId} on {Host} (accel={Accel}, maxJobs={Max})",
            _nodeId, hostname, accel, _settings.MaxParallelJobs);

        _heartbeatTimer = new Timer(SendHeartbeat, null,
            dueTime:  TimeSpan.FromSeconds(30),
            period:   TimeSpan.FromSeconds(30));
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_heartbeatTimer is not null)
            await _heartbeatTimer.DisposeAsync();

        if (_nodeId is not null)
        {
            await _db.WorkerNodes.DeleteOneAsync(
                Builders<WorkerNode>.Filter.Eq(n => n.Id, _nodeId), ct);
            _logger.LogInformation("Worker node {NodeId} deregistered.", _nodeId);
        }
    }

    private void SendHeartbeat(object? _)
    {
        if (_nodeId is null) return;

        _ = _db.WorkerNodes.UpdateOneAsync(
            Builders<WorkerNode>.Filter.Eq(n => n.Id, _nodeId),
            Builders<WorkerNode>.Update.Set(n => n.LastHeartbeat, DateTime.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        if (_heartbeatTimer is not null)
            await _heartbeatTimer.DisposeAsync();
    }
}
