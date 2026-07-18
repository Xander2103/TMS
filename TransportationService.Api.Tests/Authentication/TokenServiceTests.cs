using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Authentication;

public class TokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private static (Guid userId, Guid tenantId) Ids => (Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void AccessToken_ContainsExpectedUserTenantRoleAndPermissionClaims()
    {
        var (userId, tenantId) = Ids;
        var svc = AuthTestFactory.TokenService(new TestClock(Now));

        var token = svc.CreateAccessToken(userId, tenantId, "u@x.com", "Ann", "Lee",
            new[] { "Administrator", "Planner" }, new[] { "users.view", "employees.view" });

        // Match production: keep raw JWT claim names (sub/email/...) instead of remapping them.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token.Value, AuthTestFactory.ValidationParameters(), out _);

        Assert.Equal(userId.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(tenantId.ToString(), principal.FindFirstValue(AppClaimTypes.TenantId));
        Assert.Equal("u@x.com", principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Contains("Administrator", principal.FindAll(ClaimTypes.Role).Select(c => c.Value));
        Assert.Contains("Planner", principal.FindAll(ClaimTypes.Role).Select(c => c.Value));
        Assert.Contains("users.view", principal.FindAll(AppClaimTypes.Permission).Select(c => c.Value));
        Assert.Contains("employees.view", principal.FindAll(AppClaimTypes.Permission).Select(c => c.Value));
    }

    [Fact]
    public void AccessToken_ExpiredToken_FailsValidation()
    {
        // Issue a token dated far in the past so it is unambiguously expired at validation time
        // (the framework validator uses the real wall clock, ClockSkew is zero).
        var clock = new TestClock(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var svc = AuthTestFactory.TokenService(clock, AuthTestFactory.Options(accessMinutes: 10));
        var (userId, tenantId) = Ids;

        var token = svc.CreateAccessToken(userId, tenantId, "u@x.com", "A", "B",
            Array.Empty<string>(), Array.Empty<string>());

        Assert.Throws<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                token.Value, AuthTestFactory.ValidationParameters(validateLifetime: true), out _));
    }

    [Fact]
    public void AccessToken_WrongIssuer_FailsValidation()
    {
        var svc = AuthTestFactory.TokenService(new TestClock(Now));
        var (userId, tenantId) = Ids;
        var token = svc.CreateAccessToken(userId, tenantId, "u@x.com", "A", "B",
            Array.Empty<string>(), Array.Empty<string>());

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                token.Value, AuthTestFactory.ValidationParameters(issuer: "someone-else"), out _));
    }

    [Fact]
    public void AccessToken_WrongAudience_FailsValidation()
    {
        var svc = AuthTestFactory.TokenService(new TestClock(Now));
        var (userId, tenantId) = Ids;
        var token = svc.CreateAccessToken(userId, tenantId, "u@x.com", "A", "B",
            Array.Empty<string>(), Array.Empty<string>());

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                token.Value, AuthTestFactory.ValidationParameters(audience: "wrong-aud"), out _));
    }

    [Fact]
    public void AccessToken_TamperedSignature_FailsValidation()
    {
        var svc = AuthTestFactory.TokenService(new TestClock(Now));
        var (userId, tenantId) = Ids;
        var token = svc.CreateAccessToken(userId, tenantId, "u@x.com", "A", "B",
            Array.Empty<string>(), Array.Empty<string>());

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                token.Value,
                AuthTestFactory.ValidationParameters(signingKey: "a-totally-different-signing-key-32bytes!!"),
                out _));
    }

    [Fact]
    public void RefreshToken_HashIsDeterministicAndNotThePlainValue()
    {
        var svc = AuthTestFactory.TokenService(new TestClock(Now));
        var pair = svc.CreateRefreshToken();

        Assert.NotEqual(pair.Value, pair.Hash);
        Assert.Equal(pair.Hash, svc.HashRefreshToken(pair.Value));
    }
}
