using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Muxity.Api.Services.Auth;

/// <summary>
/// Validates an OIDC ID token issued by Google or Microsoft by fetching the
/// provider's JWKS via its discovery document and verifying the signature,
/// audience, and lifetime.
/// </summary>
public class OidcValidationService
{
    private static readonly Dictionary<string, string> DiscoveryUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google"]    = "https://accounts.google.com/.well-known/openid-configuration",
        ["microsoft"] = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
    };

    private readonly IConfiguration _config;
    private readonly ILogger<OidcValidationService> _logger;

    // Cache the configuration manager per provider so JWKS is refreshed automatically.
    private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new();

    public OidcValidationService(IConfiguration config, ILogger<OidcValidationService> logger)
    {
        _config = config;
        _logger = logger;

        foreach (var (provider, discoveryUrl) in DiscoveryUrls)
        {
            _managers[provider] = new ConfigurationManager<OpenIdConnectConfiguration>(
                discoveryUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
        }
    }

    /// <summary>
    /// Validates the ID token and returns the claims principal on success.
    /// Throws <see cref="SecurityTokenException"/> if validation fails.
    /// </summary>
    public async Task<ClaimsPrincipal> ValidateAsync(string provider, string idToken, CancellationToken ct = default)
    {
        if (!_managers.TryGetValue(provider, out var manager))
            throw new ArgumentException($"Unsupported provider: {provider}");

        var oidcConfig = await manager.GetConfigurationAsync(ct);

        var clientId = _config[$"Oidc:{provider}:ClientId"]
            ?? throw new InvalidOperationException($"Oidc:{provider}:ClientId is not configured.");

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer            = true,
            ValidIssuers              = oidcConfig.Issuer is not null ? [oidcConfig.Issuer] : null,
            ValidateAudience          = true,
            ValidAudience             = clientId,
            ValidateLifetime          = true,
            ValidateIssuerSigningKey  = true,
            IssuerSigningKeys         = oidcConfig.SigningKeys,
            ClockSkew                 = TimeSpan.FromMinutes(2),
        };

        // Microsoft multi-tenant tokens carry a /common/ issuer; allow the per-tenant form too.
        if (provider.Equals("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            validationParameters.ValidateIssuer = false;
        }

        var handler = new JwtSecurityTokenHandler();
        try
        {
            return handler.ValidateToken(idToken, validationParameters, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ID token validation failed for provider {Provider}", provider);
            throw new SecurityTokenException($"Invalid {provider} ID token.", ex);
        }
    }
}
