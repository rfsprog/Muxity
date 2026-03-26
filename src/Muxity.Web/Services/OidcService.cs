using Microsoft.JSInterop;
using Muxity.Web.Models;

namespace Muxity.Web.Services;

/// <summary>
/// Implements PKCE-based OIDC authorization code flow for Google and Microsoft.
/// JavaScript interop handles crypto (SubtleCrypto) and sessionStorage for the
/// code_verifier, keeping it out of Blazor component state.
/// </summary>
public class OidcService
{
    private readonly IJSRuntime _js;
    private readonly ApiClient _api;
    private readonly AuthStateService _auth;
    private readonly IConfiguration _config;

    public OidcService(IJSRuntime js, ApiClient api, AuthStateService auth, IConfiguration config)
    {
        _js     = js;
        _api    = api;
        _auth   = auth;
        _config = config;
    }

    /// <summary>
    /// Builds the OIDC authorization URL and redirects the browser.
    /// Stores code_verifier and provider in sessionStorage for the callback.
    /// </summary>
    public async Task BeginLoginAsync(string provider)
    {
        var clientId  = _config[$"Oidc:{provider}:ClientId"]!;
        var authority = _config[$"Oidc:{provider}:Authority"]!;
        var redirectUri = await _js.InvokeAsync<string>("oidcHelper.getRedirectUri");

        var verifier   = await _js.InvokeAsync<string>("oidcHelper.generateCodeVerifier");
        var challenge  = await _js.InvokeAsync<string>("oidcHelper.generateCodeChallenge", verifier);
        var state      = Guid.NewGuid().ToString("N");

        await _js.InvokeVoidAsync("oidcHelper.saveToSession", "code_verifier", verifier);
        await _js.InvokeVoidAsync("oidcHelper.saveToSession", "oidc_state",    state);
        await _js.InvokeVoidAsync("oidcHelper.saveToSession", "oidc_provider", provider);

        var authUrl = $"{authority}/o/oauth2/v2/auth" + // Google; Microsoft uses /oauth2/v2.0/authorize
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString("openid email profile")}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256" +
            $"&state={state}";

        // Use provider-specific authorization endpoint
        if (provider.Equals("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            authUrl = $"{authority}/oauth2/v2.0/authorize" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString("openid email profile")}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&code_challenge={challenge}" +
                $"&code_challenge_method=S256" +
                $"&state={state}";
        }

        await _js.InvokeVoidAsync("oidcHelper.redirect", authUrl);
    }

    /// <summary>
    /// Called from <c>/login/callback</c>. Exchanges the auth code for an ID token,
    /// then calls the Muxity API to issue application tokens.
    /// Returns null on failure.
    /// </summary>
    public async Task<AuthResponse?> HandleCallbackAsync(string code, string state)
    {
        var savedState = await _js.InvokeAsync<string?>("oidcHelper.getFromSession", "oidc_state");
        if (savedState != state) return null;

        var verifier  = await _js.InvokeAsync<string?>("oidcHelper.getFromSession", "code_verifier");
        var provider  = await _js.InvokeAsync<string?>("oidcHelper.getFromSession", "oidc_provider");
        if (verifier is null || provider is null) return null;

        await _js.InvokeVoidAsync("oidcHelper.clearSession",
            "code_verifier", "oidc_state", "oidc_provider");

        var clientId    = _config[$"Oidc:{provider}:ClientId"]!;
        var authority   = _config[$"Oidc:{provider}:Authority"]!;
        var redirectUri = await _js.InvokeAsync<string>("oidcHelper.getRedirectUri");
        var tokenEndpoint = provider.Equals("microsoft", StringComparison.OrdinalIgnoreCase)
            ? $"{authority}/oauth2/v2.0/token"
            : "https://oauth2.googleapis.com/token";

        // Exchange code for tokens at the provider's token endpoint
        var idToken = await _js.InvokeAsync<string?>(
            "oidcHelper.exchangeCode",
            tokenEndpoint, clientId, code, verifier, redirectUri);

        if (idToken is null) return null;

        var authResponse = await _api.CallbackAsync(provider, idToken);
        if (authResponse is null) return null;

        _auth.SetSession(
            authResponse.AccessToken,
            authResponse.RefreshToken,
            authResponse.User.Id,
            authResponse.User.Email,
            authResponse.User.DisplayName);

        return authResponse;
    }
}
