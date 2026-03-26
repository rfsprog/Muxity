using Muxity.Web.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Muxity.Web.Services;

/// <summary>
/// Typed wrapper over the named "MuxityApi" HttpClient.
/// Attaches the JWT bearer token from <see cref="AuthStateService"/> and
/// automatically retries once with a refreshed token on 401.
/// </summary>
public class ApiClient
{
    private readonly IHttpClientFactory _factory;
    private readonly AuthStateService _auth;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ApiClient(IHttpClientFactory factory, AuthStateService auth)
    {
        _factory = factory;
        _auth    = auth;
    }

    // ── Auth endpoints ────────────────────────────────────────────────────────

    public Task<AuthResponse?> CallbackAsync(string provider, string idToken, CancellationToken ct = default)
        => PostAnonymousAsync<AuthResponse>("/auth/callback",
            new CallbackRequest(provider, idToken), ct);

    public Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => PostAnonymousAsync<AuthResponse>("/auth/refresh",
            new { RefreshToken = refreshToken }, ct);

    // ── Video endpoints ───────────────────────────────────────────────────────

    public Task<PagedResponse<VideoSummary>?> ListVideosAsync(
        int page = 1, int pageSize = 20, string? status = null, string? visibility = null,
        CancellationToken ct = default)
    {
        var qs = $"?page={page}&pageSize={pageSize}"
            + (status     is not null ? $"&status={status}"         : "")
            + (visibility is not null ? $"&visibility={visibility}" : "");
        return GetAsync<PagedResponse<VideoSummary>>($"/videos{qs}", ct);
    }

    public Task<VideoDetail?> GetVideoAsync(string id, CancellationToken ct = default)
        => GetAsync<VideoDetail>($"/videos/{id}", ct);

    public Task<PagedResponse<VideoSummary>?> SearchVideosAsync(
        string q, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => GetAsync<PagedResponse<VideoSummary>>($"/videos/search?q={Uri.EscapeDataString(q)}&page={page}&pageSize={pageSize}", ct);

    public Task<VideoDetail?> UpdateVideoAsync(
        string id, string? title, string? description, string? visibility,
        CancellationToken ct = default)
        => PatchAsync<VideoDetail>($"/videos/{id}",
            new { Title = title, Description = description, Visibility = visibility }, ct);

    public Task DeleteVideoAsync(string id, CancellationToken ct = default)
        => DeleteAsync($"/videos/{id}", ct);

    public Task<UploadStatusResponse?> GetUploadStatusAsync(string id, CancellationToken ct = default)
        => GetAsync<UploadStatusResponse>($"/videos/{id}/upload-status", ct);

    public Task<StreamingKeyResponse?> GetStreamingKeyAsync(string id, CancellationToken ct = default)
        => GetAsync<StreamingKeyResponse>($"/videos/{id}/streaming-key", ct);

    // ── HTTP primitives ───────────────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var response = await SendWithAuthAsync(HttpMethod.Get, path, null, ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct)
            : default;
    }

    public async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var response = await SendWithAuthAsync(HttpMethod.Post, path, body, ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct)
            : default;
    }

    public async Task<T?> PatchAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var response = await SendWithAuthAsync(HttpMethod.Patch, path, body, ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct)
            : default;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
        => await SendWithAuthAsync(HttpMethod.Delete, path, null, ct);

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendWithAuthAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var client   = _factory.CreateClient("MuxityApi");
        var request  = BuildRequest(method, path, body);
        AttachBearer(request);

        var response = await client.SendAsync(request, ct);

        // On 401 try to refresh once, then retry
        if (response.StatusCode == HttpStatusCode.Unauthorized && _auth.GetRefreshToken() is { } rt)
        {
            var refreshed = await RefreshAsync(rt, ct);
            if (refreshed is not null)
            {
                _auth.SetSession(
                    refreshed.AccessToken, refreshed.RefreshToken,
                    refreshed.User.Id, refreshed.User.Email, refreshed.User.DisplayName);

                request  = BuildRequest(method, path, body);
                AttachBearer(request);
                response = await client.SendAsync(request, ct);
            }
        }

        return response;
    }

    private async Task<T?> PostAnonymousAsync<T>(string path, object body, CancellationToken ct)
    {
        var client   = _factory.CreateClient("MuxityApi");
        var request  = BuildRequest(HttpMethod.Post, path, body);
        var response = await client.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct)
            : default;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body)
    {
        var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    private void AttachBearer(HttpRequestMessage req)
    {
        var token = _auth.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}
