using Microsoft.Extensions.Options;
using Muxity.Shared.Data;
using Muxity.Shared.Storage;
using Muxity.Transcoder.Services;
using Muxity.Transcoder.Workers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------
        services.Configure<MongoDbSettings>(ctx.Configuration.GetSection("MongoDB"));
        services.Configure<StorageSettings>(ctx.Configuration.GetSection("Storage"));
        services.Configure<TranscoderSettings>(ctx.Configuration.GetSection("Transcoder"));

        // ------------------------------------------------------------------
        // MongoDB
        // ------------------------------------------------------------------
        services.AddSingleton<MongoDbContext>();

        // ------------------------------------------------------------------
        // Storage — same Local/S3 switch as the API
        // ------------------------------------------------------------------
        var storageProvider = ctx.Configuration.GetValue<string>("Storage:Provider", "Local");
        if (storageProvider!.Equals("S3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IStorageProvider, S3StorageProvider>();
        else
            services.AddSingleton<IStorageProvider, LocalStorageProvider>();

        // ------------------------------------------------------------------
        // FFmpeg service (hardware accel detection is lazy + cached)
        // ------------------------------------------------------------------
        services.AddSingleton<FfmpegService>();

        // ------------------------------------------------------------------
        // Worker registration (IHostedService — registers node in MongoDB,
        // sends heartbeat, deregisters on shutdown)
        // ------------------------------------------------------------------
        services.AddSingleton<WorkerRegistrationService>();
        services.AddHostedService(sp => sp.GetRequiredService<WorkerRegistrationService>());

        // ------------------------------------------------------------------
        // RabbitMQ consumer (IHostedService — opens connection + channel)
        // ------------------------------------------------------------------
        services.AddSingleton<JobConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<JobConsumer>());

        // ------------------------------------------------------------------
        // Main worker loop
        // ------------------------------------------------------------------
        services.AddHostedService<TranscoderWorker>();
    })
    .Build();

await host.RunAsync();
