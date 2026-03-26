namespace Muxity.Web.Services;

/// <summary>
/// Holds the current user's auth state in memory for the WASM session.
/// Tokens are stored only in memory (not localStorage) to reduce XSS exposure.
/// Phase 5 will add secure cookie + silent refresh logic.
/// </summary>
public class AuthStateService
{
    private string? _accessToken;
    private string? _refreshToken;

    public string? UserId      { get; private set; }
    public string? Email       { get; private set; }
    public string? DisplayName { get; private set; }
    public bool    IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public event Action? OnChange;

    public void SetSession(
        string accessToken,
        string refreshToken,
        string userId,
        string email,
        string displayName)
    {
        _accessToken  = accessToken;
        _refreshToken = refreshToken;
        UserId        = userId;
        Email         = email;
        DisplayName   = displayName;
        OnChange?.Invoke();
    }

    public void ClearSession()
    {
        _accessToken  = null;
        _refreshToken = null;
        UserId        = null;
        Email         = null;
        DisplayName   = null;
        OnChange?.Invoke();
    }

    public string? GetAccessToken()  => _accessToken;
    public string? GetRefreshToken() => _refreshToken;
}
