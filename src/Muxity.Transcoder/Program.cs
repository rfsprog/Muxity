using Muxity.Shared.Data;
using Muxity.Transcoder.Workers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<MongoDbSettings>(ctx.Configuration.GetSection("MongoDB"));
        services.AddSingleton<MongoDbContext>();
        services.AddHostedService<TranscoderWorker>();
    })
    .Build();

await host.RunAsync();
