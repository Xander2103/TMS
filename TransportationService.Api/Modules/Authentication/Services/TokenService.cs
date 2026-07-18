using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TransportationService.Api.Modules.Authentication.Services;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;

    public TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(
        Guid userId,
        Guid tenantId,
        string email,
        string firstName,
        string lastName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresUtc = nowUtc.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.GivenName, firstName),
            new(JwtRegisteredClaimNames.FamilyName, lastName),
            new(AppClaimTypes.TenantId, tenantId.ToString()),
        };

        foreach (var role in roles.Distinct())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissions.Distinct())
        {
            claims.Add(new Claim(AppClaimTypes.Permission, permission));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: expiresUtc,
            signingCredentials: _signingCredentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expiresUtc);
    }

    public RefreshTokenPair CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Convert.ToBase64String(bytes);
        var expiresUtc = _timeProvider.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);
        return new RefreshTokenPair(raw, HashRefreshToken(raw), expiresUtc);
    }

    public string HashRefreshToken(string rawValue)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawValue));
        return Convert.ToBase64String(hash);
    }
}
