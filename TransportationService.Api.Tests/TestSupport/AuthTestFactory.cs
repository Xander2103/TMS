using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>Shared builders for authentication tests (options, token service, validation params).</summary>
public static class AuthTestFactory
{
    public const string Issuer = "test-issuer";
    public const string Audience = "test-audience";
    public const string SigningKey = "test-signing-key-that-is-at-least-32-bytes!!";

    public static JwtOptions Options(int accessMinutes = 60, int refreshDays = 14) => new()
    {
        Issuer = Issuer,
        Audience = Audience,
        SigningKey = SigningKey,
        AccessTokenMinutes = accessMinutes,
        RefreshTokenDays = refreshDays,
    };

    public static TokenService TokenService(TimeProvider clock, JwtOptions? options = null) =>
        new(new OptionsWrapper<JwtOptions>(options ?? Options()), clock);

    public static TokenValidationParameters ValidationParameters(
        string? issuer = null, string? audience = null, string? signingKey = null,
        bool validateLifetime = false) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer ?? Issuer,
        ValidateAudience = true,
        ValidAudience = audience ?? Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
        ValidateLifetime = validateLifetime,
        ClockSkew = TimeSpan.Zero,
    };
}
