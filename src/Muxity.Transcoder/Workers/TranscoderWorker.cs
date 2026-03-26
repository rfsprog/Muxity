using Muxity.Shared.Data;

namespace Muxity.Transcoder.Workers;

/// <summary>
/// Placeholder worker that will be fully implemented in Phase 3.
/// Connects to RabbitMQ, claims TranscodeJob documents from MongoDB,
/// runs FFmpeg with hardware acceleration, and writes HLS segments to storage.
/// </summary>
public class TranscoderWorker : BackgroundService
{
    private readonly ILogger<TranscoderWorker> _logger;
    private readonly MongoDbContext _db;
    private readonly IConfiguration _config;

    public TranscoderWorker(ILogger<TranscoderWorker> logger, MongoDbContext db, IConfiguration config)
    {
        _logger = logger;
        _db     = db;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transcoder worker starting. HardwareAccel={Accel}, MaxJobs={Max}",
            _config["Transcoder:HardwareAccel"],
            _config["Transcoder:MaxParallelJobs"]);

        // Full implementation in Phase 3:
        //   - Connect to RabbitMQ and consume transcode_jobs queue
        //   - Atomically claim a TranscodeJob in MongoDB
        //   - Run FFmpeg pipeline (QSV / NVENC / software fallback)
        //   - Output HLS segments to storage
        //   - Generate thumbnail
        //   - Update Video.Status = Ready
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
