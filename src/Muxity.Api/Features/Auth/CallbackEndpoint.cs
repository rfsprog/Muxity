using FastEndpoints;
using MongoDB.Driver;
using Muxity.Api.Services.Auth;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.Security.Claims;

namespace Muxity.Api.Features.Auth;

public class CallbackRequest
{
    /// <summary>google | microsoft</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The raw OIDC ID token obtained by the Blazor WASM client.</summary>
    public string IdToken { get; set; } = string.Empty;
}

public class CallbackResponse
{
    public string AccessToken  { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt  { get; set; }
    public UserDto User        { get; set; } = new();
}

public class UserDto
{
    public string Id          { get; set; } = string.Empty;
    public string Email       { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Provider    { get; set; } = string.Empty;
}

public class CallbackEndpoint : Endpoint<CallbackRequest, CallbackResponse>
{
    private readonly OidcValidationService _oidc;
    private readonly TokenService _tokens;
    private readonly MongoDbContext _db;

    public CallbackEndpoint(OidcValidationService oidc, TokenService tokens, MongoDbContext db)
    {
        _oidc   = oidc;
        _tokens = tokens;
        _db     = db;
    }

    public override void Configure()
    {
        Post("/auth/callback");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary     = "Exchange a provider ID token for a Muxity access + refresh token pair.";
            s.Description = "Called by the Blazor WASM client after completing the OIDC flow with Google or Microsoft.";
        });
    }

    public override async Task HandleAsync(CallbackRequest req, CancellationToken ct)
    {
        ClaimsPrincipal principal;
        try
        {
            principal = await _oidc.ValidateAsync(req.Provider, req.IdToken, ct);
        }
        catch
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var sub   = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub")
                    ?? string.Empty;
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email")
                    ?? string.Empty;
        var name  = principal.FindFirstValue("name")
                    ?? principal.FindFirstValue(ClaimTypes.Name)
                    ?? email;

        if (string.IsNullOrEmpty(sub))
        {
            AddError("IdToken", "ID token is missing the 'sub' claim.");
            await SendErrorsAsync(400, ct);
            return;
        }

        // Upsert user — match by provider + externalId
        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Eq(u => u.Provider, req.Provider.ToLowerInvariant()),
            Builders<User>.Filter.Eq(u => u.ExternalId, sub));

        var update = Builders<User>.Update
            .SetOnInsert(u => u.Id,         MongoDB.Bson.ObjectId.GenerateNewId().ToString())
            .SetOnInsert(u => u.Provider,   req.Provider.ToLowerInvariant())
            .SetOnInsert(u => u.ExternalId, sub)
            .SetOnInsert(u => u.CreatedAt,  DateTime.UtcNow)
            .Set(u => u.Email,              email)
            .Set(u => u.DisplayName,        name)
            .Set(u => u.UpdatedAt,          DateTime.UtcNow);

        var options = new FindOneAndUpdateOptions<User>
        {
            IsUpsert       = true,
            ReturnDocument = ReturnDocument.After,
        };

        var user = await _db.Users.FindOneAndUpdateAsync(filter, update, options, ct);

        var pair = await _tokens.IssueAndPersistAsync(user, ct);

        await SendOkAsync(new CallbackResponse
        {
            AccessToken  = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresAt    = pair.AccessTokenExpiry,
            User         = new UserDto
            {
                Id          = user.Id,
                Email       = user.Email,
                DisplayName = user.DisplayName,
                Provider    = user.Provider,
            },
        }, ct);
    }
}
