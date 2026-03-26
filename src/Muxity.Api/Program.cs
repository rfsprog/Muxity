using FastEndpoints;
using FastEndpoints.Security;
using Microsoft.IdentityModel.Tokens;
using Muxity.Api.Services.Auth;
using Muxity.Shared.Data;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Configuration
// ------------------------------------------------------------------
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDB"));

// ------------------------------------------------------------------
// MongoDB
// ------------------------------------------------------------------
builder.Services.AddSingleton<MongoDbContext>();

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
