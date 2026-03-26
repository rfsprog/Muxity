using FastEndpoints;
using MongoDB.Driver;
using Muxity.Shared.Data;
using System.IdentityModel.Tokens.Jwt;

namespace Muxity.Api.Features.Auth;

public class MeEndpoint : EndpointWithoutRequest<UserDto>
{
    private readonly MongoDbContext _db;

    public MeEndpoint(MongoDbContext db) => _db = db;

    public override void Configure()
    {
        Get("/auth/me");
        // Requires a valid JWT bearer token
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var user = await _db.Users
            .Find(u => u.Id == userId)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(new UserDto
        {
            Id          = user.Id,
            Email       = user.Email,
            DisplayName = user.DisplayName,
            Provider    = user.Provider,
        }, ct);
    }
}
