using System.ComponentModel.DataAnnotations;

namespace TransportationService.Api.Modules.Authentication;

/// <summary>
/// Sign-in and session-security knobs. Defaults are safe: an attacker needs many attempts to make
/// progress, while a legitimate user is never locked out permanently (the lockout always expires,
/// so an attacker cannot weaponise it into a lasting denial of service on someone else's account).
/// </summary>
public sealed class AuthenticationSecurityOptions
{
    public const string SectionName = "Security:Authentication";

    /// <summary>Consecutive failures before the account is temporarily locked.</summary>
    [Range(3, 50)]
    public int MaxFailedLoginAttempts { get; set; } = 8;

    /// <summary>Base lockout window; doubles per additional lockout up to the maximum.</summary>
    [Range(1, 1440)]
    public int BaseLockoutMinutes { get; set; } = 5;

    /// <summary>Ceiling for the exponential lockout window (keeps DoS-by-lockout bounded).</summary>
    [Range(1, 1440)]
    public int MaxLockoutMinutes { get; set; } = 60;

    /// <summary>Maximum number of concurrently active refresh tokens (sessions) per user.</summary>
    [Range(1, 100)]
    public int MaxActiveSessionsPerUser { get; set; } = 10;

    /// <summary>Retention for expired/revoked refresh tokens before the purge sweep removes them.</summary>
    [Range(1, 365)]
    public int RefreshTokenRetentionDays { get; set; } = 30;
}
