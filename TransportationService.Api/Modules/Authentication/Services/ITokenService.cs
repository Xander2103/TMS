using System.Security.Claims;

namespace TransportationService.Api.Modules.Authentication.Services;

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

/// <summary>Raw refresh-token value (returned to the client once) plus its stored hash.</summary>
public sealed record RefreshTokenPair(string Value, string Hash, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(
        Guid userId,
        Guid tenantId,
        string email,
        string firstName,
        string lastName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions);

    RefreshTokenPair CreateRefreshToken();

    /// <summary>Deterministic SHA-256 (Base64) hash used to look up a presented refresh token.</summary>
    string HashRefreshToken(string rawValue);
}
