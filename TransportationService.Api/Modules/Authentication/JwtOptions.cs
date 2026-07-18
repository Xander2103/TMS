using System.ComponentModel.DataAnnotations;

namespace TransportationService.Api.Modules.Authentication;

/// <summary>
/// Strongly-typed JWT configuration bound from the "Jwt" configuration section.
/// The signing key is a secret and must be supplied via configuration/environment
/// outside Development; startup fails fast when it is missing (see AuthenticationServiceCollectionExtensions).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric HMAC-SHA256 signing key. Must be at least 32 bytes.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}
