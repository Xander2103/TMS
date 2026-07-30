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

    /// <summary>
    /// Previous signing key, accepted for VALIDATION only during a controlled rotation window so
    /// rotating the key does not log everyone out instantly. Remove it once the window has passed.
    /// </summary>
    [MinLength(32)]
    public string? PreviousSigningKey { get; set; }

    /// <summary>Key id (kid) stamped on issued tokens; lets validators pick the right key.</summary>
    public string KeyId { get; set; } = "primary";

    /// <summary>Key id of <see cref="PreviousSigningKey"/> during rotation.</summary>
    public string PreviousKeyId { get; set; } = "previous";

    /// <summary>
    /// Short-lived by design: server-side revocation (security stamp) closes the remaining window,
    /// and the refresh flow keeps sessions usable. Kept configurable for operational tuning.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}
