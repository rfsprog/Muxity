using FastEndpoints;
using Muxity.Api.Services.Auth;

namespace Muxity.Api.Features.Auth;

public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshResponse
{
    public string AccessToken  { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt  { get; set; }
}

public class RefreshEndpoint : Endpoint<RefreshRequest, RefreshResponse>
{
    private readonly TokenService _tokens;

    public RefreshEndpoint(TokenService tokens) => _tokens = tokens;

    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
        Summary(s => s.Summary = "Rotate a refresh token and obtain a new access + refresh token pair.");
    }

    public override async Task HandleAsync(RefreshRequest req, CancellationToken ct)
    {
        var result = await _tokens.RotateRefreshTokenAsync(req.RefreshToken, ct);

        if (result is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var (_, pair) = result.Value;

        await SendOkAsync(new RefreshResponse
        {
            AccessToken  = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresAt    = pair.AccessTokenExpiry,
        }, ct);
    }
}
