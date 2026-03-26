using FastEndpoints;
using FastEndpoints.Security;
using Microsoft.AspNetCore.RateLimiting;
using Muxity.Shared.Data;
using Muxity.Shared.Storage;
using Muxity.Streaming.Services;
using Muxity.Streaming.Services.Cdn;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Configuration
// ------------------------------------------------------------------
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<CdnSettings>(builder.Configuration.GetSection("Cdn"));

// ------------------------------------------------------------------
// MongoDB
// ------------------------------------------------------------------
builder.Services.AddSingleton<MongoDbContext>();

// ------------------------------------------------------------------
// Storage (same Local/S3 switch as the API)
// ------------------------------------------------------------------
var storageProvider = builder.Configuration.GetValue<string>("Storage:Provider", "Local");
if (storageProvider!.Equals("S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IStorageProvider, S3StorageProvider>();
else
    builder.Services.AddSingleton<IStorageProvider, LocalStorageProvider>();

// ------------------------------------------------------------------
// CDN provider
// ------------------------------------------------------------------
var cdnProvider = builder.Configuration.GetValue<string>("Cdn:Provider", "Passthrough");
switch (cdnProvider!.ToLowerInvariant())
{
    case "cloudfront":
        builder.Services.AddSingleton<ICdnProvider, CloudFrontCdnProvider>();
        break;
    case "cloudflare":
        builder.Services.AddSingleton<ICdnProvider, CloudflareCdnProvider>();
        break;
    default:
        builder.Services.AddSingleton<ICdnProvider, PassthroughCdnProvider>();
        break;
}

// ------------------------------------------------------------------
// Streaming key resolution with in-memory caching
// ------------------------------------------------------------------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<StreamingKeyService>();

// ------------------------------------------------------------------
// JWT bearer auth
// ------------------------------------------------------------------
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret must be set in configuration.");

builder.Services
    .AddAuthenticationJwtBearer(s => s.SigningKey = jwtSecret)
    .AddAuthorization();

// ------------------------------------------------------------------
// FastEndpoints
// ------------------------------------------------------------------
builder.Services.AddFastEndpoints();

// ------------------------------------------------------------------
// Rate limiting — sliding window per streaming key
// Configurable: RateLimit:RequestsPerWindow + RateLimit:WindowSeconds
// ------------------------------------------------------------------
var requestsPerWindow = builder.Configuration.GetValue<int>("RateLimit:RequestsPerWindow", 300);
var windowSeconds     = builder.Configuration.GetValue<int>("RateLimit:WindowSeconds", 10);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("streaming-key", httpContext =>
    {
        // Partition by streaming key in the route
        var key = httpContext.GetRouteValue("key")?.ToString() ?? "anonymous";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit          = requestsPerWindow,
            Window               = TimeSpan.FromSeconds(windowSeconds),
            SegmentsPerWindow    = 5,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 0,
        });
    });
});

// ------------------------------------------------------------------
// CORS — allow Blazor WASM origin
// ------------------------------------------------------------------
var webOrigin = builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(webOrigin)
     .AllowAnyHeader()
     .WithMethods("GET")
     .AllowCredentials()));

var app = builder.Build();

// ------------------------------------------------------------------
// Middleware pipeline
// ------------------------------------------------------------------
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    // Apply the streaming-key rate limit policy to all /stream/* routes
    c.Endpoints.Configurator = ep =>
    {
        if (ep.Routes.Any(r => r.StartsWith("/stream/")))
            ep.Options(o => o.RequireRateLimiting("streaming-key"));
    };
});

app.Run();
