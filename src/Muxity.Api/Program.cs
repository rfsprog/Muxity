using FastEndpoints;
using FastEndpoints.Security;
using Muxity.Api.Services.Auth;
using Muxity.Api.Services.Messaging;
using Muxity.Api.Services.Storage;
using Muxity.Shared.Data;
using Muxity.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Configuration bindings
// ------------------------------------------------------------------
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));

// ------------------------------------------------------------------
// MongoDB
// ------------------------------------------------------------------
builder.Services.AddSingleton<MongoDbContext>();

// ------------------------------------------------------------------
// Storage — Local or S3 depending on Storage:Provider config
// ------------------------------------------------------------------
var storageProvider = builder.Configuration.GetValue<string>("Storage:Provider", "Local");
if (storageProvider!.Equals("S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IStorageProvider, S3StorageProvider>();
else
    builder.Services.AddSingleton<IStorageProvider, LocalStorageProvider>();

// ------------------------------------------------------------------
// RabbitMQ publisher
// ------------------------------------------------------------------
builder.Services.AddSingleton<RabbitMqPublisher>();

// ------------------------------------------------------------------
// Auth services
// ------------------------------------------------------------------
builder.Services.AddSingleton<OidcValidationService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpClient();

// ------------------------------------------------------------------
// JWT bearer auth (validates our own issued tokens)
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
// Multipart upload — allow large files (streamed, not buffered)
// ------------------------------------------------------------------
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    var maxBytes = builder.Configuration.GetValue<long>("Storage:MaxFileSizeBytes", 10L * 1024 * 1024 * 1024);
    o.MultipartBodyLengthLimit = maxBytes;
});
builder.WebHost.ConfigureKestrel(k =>
{
    var maxBytes = builder.Configuration.GetValue<long>("Storage:MaxFileSizeBytes", 10L * 1024 * 1024 * 1024);
    k.Limits.MaxRequestBodySize = maxBytes;
});

// ------------------------------------------------------------------
// CORS — allow Blazor WASM origin
// ------------------------------------------------------------------
var webOrigin = builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(webOrigin)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

var app = builder.Build();

// ------------------------------------------------------------------
// Ensure MongoDB indexes on startup
// ------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await db.EnsureIndexesAsync();
}

// ------------------------------------------------------------------
// Middleware pipeline
// ------------------------------------------------------------------
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();

app.Run();
