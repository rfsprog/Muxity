using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Muxity.Web;
using Muxity.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ------------------------------------------------------------------
// Configuration — reads wwwroot/appsettings.json
// ------------------------------------------------------------------
var apiBase      = builder.Configuration["Api:BaseUrl"]      ?? "http://localhost:5100";
var streamingBase = builder.Configuration["Api:StreamingUrl"] ?? "http://localhost:5200";

// ------------------------------------------------------------------
// Named HttpClients
// ------------------------------------------------------------------
builder.Services.AddHttpClient("MuxityApi", c =>
    c.BaseAddress = new Uri(apiBase));

builder.Services.AddHttpClient("MuxityStreaming", c =>
    c.BaseAddress = new Uri(streamingBase));

// ------------------------------------------------------------------
// Auth state
// ------------------------------------------------------------------
builder.Services.AddScoped<AuthStateService>();

await builder.Build().RunAsync();
