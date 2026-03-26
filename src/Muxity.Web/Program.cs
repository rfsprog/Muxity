using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Muxity.Web;
using Muxity.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase       = builder.Configuration["Api:BaseUrl"]       ?? "http://localhost:5100";
var streamingBase = builder.Configuration["Api:StreamingUrl"]  ?? "http://localhost:5200";

builder.Services.AddHttpClient("MuxityApi",      c => c.BaseAddress = new Uri(apiBase));
builder.Services.AddHttpClient("MuxityStreaming", c => c.BaseAddress = new Uri(streamingBase));

builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<OidcService>();

await builder.Build().RunAsync();
