using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Muxity.Shared.Data;
using Muxity.Shared.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Muxity.Api.Services.Auth;

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiry);

public class TokenService
{
    private readonly IConfiguration _config;
    private readonly MongoDbContext _db;

    public TokenService(IConfiguration config, MongoDbContext db)
    {
        _config = config;
        _db = db;
    }

    public TokenPair IssueTokens(User user)
    {
        var accessToken  = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();

        var refreshExpiry = DateTime.UtcNow.AddDays(
            _config.GetValue<int>("Jwt:RefreshTokenExpiryDays", 30));

        return new TokenPair(accessToken.Token, refreshToken, accessToken.Expiry);
    }

    public async Task<TokenPair> IssueAndPersistAsync(User user, CancellationToken ct = default)
    {
        var pair = IssueTokens(user);

        var refreshExpiry = DateTime.UtcNow.AddDays(
            _config.GetValue<int>("Jwt:RefreshTokenExpiryDays", 30));

        var doc = new RefreshToken
        {
            UserId    = user.Id,
            Token     = pair.RefreshToken,
            ExpiresAt = refreshExpiry,
        };

        await _db.RefreshTokens.InsertOneAsync(doc, cancellationToken: ct);
        return pair;
    }

    public async Task<(User user, TokenPair pair)?> RotateRefreshTokenAsync(
        string rawRefreshToken, CancellationToken ct = default)
    {
        var filter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(r => r.Token, rawRefreshToken),
            Builders<RefreshToken>.Filter.Eq(r => r.Used, false),
            Builders<RefreshToken>.Filter.Gt(r => r.ExpiresAt, DateTime.UtcNow));

        // Atomic mark-as-used
        var update = Builders<RefreshToken>.Update.Set(r => r.Used, true);
        var existing = await _db.RefreshTokens.FindOneAndUpdateAsync(filter, update,
            cancellationToken: ct);

        if (existing is null)
            return null;

        var user = await _db.Users
            .Find(u => u.Id == existing.UserId)
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return null;

        var newPair = await IssueAndPersistAsync(user, ct);
        return (user, newPair);
    }

    // -------------------------------------------------------------------------

    private (string Token, DateTime Expiry) GenerateAccessToken(User user)
    {
        var key     = GetSigningKey();
        var expiry  = DateTime.UtcNow.AddMinutes(_config.GetValue<int>("Jwt:AccessTokenExpiryMinutes", 15));
        var claims  = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name",                        user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expiry,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}
