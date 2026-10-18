namespace TransportationService.Api.Modules.Identity.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>The address as typed (display / mail delivery).</summary>
    public string Email
    {
        get => _email;
        set
        {
            _email = value;
            NormalizedEmail = NormalizeEmail(value);
        }
    }

    private string _email = string.Empty;

    /// <summary>
    /// MD-H11 (master-data acceptance 2026-10-18): the account identity — trimmed and lower-cased,
    /// so "Test.User@Example.test" and "test.user@example.test" are ONE account. Uniqueness, login
    /// and password reset all use this; e-mail addresses are case-insensitive in practice.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? CustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }

    /// <summary>
    /// UI/e-mail language preference (nl/fr/en). Null = not chosen yet; display falls back to
    /// the browser language (portal frontend) → customer default language → nl.
    /// </summary>
    public string? PreferredLanguageCode { get; set; }

    /// <summary>
    /// Persoonlijke weergavevoorkeur van de UI (light/dark/system, zie
    /// <see cref="Common.ThemePreferences"/>). Null = nog niet gekozen → de frontend volgt het
    /// systeem. Hoort bij de gebruiker, niet bij de tenant: twee collega's van hetzelfde bedrijf
    /// kiezen onafhankelijk van elkaar.
    /// </summary>
    public string? ThemePreference { get; set; }

    /// <summary>Forces a password change at the next login (temporary/admin-set credentials).</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Server-side session-revocation stamp. It is embedded in every access token and verified
    /// on each authenticated request; rotating it (on an administrative password reset, and any
    /// future block/deactivate) immediately invalidates all outstanding access tokens for this
    /// user without waiting for their short lifetime to elapse.
    /// </summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    /// <summary>Consecutive failed sign-in attempts since the last success (account lockout).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>When set and in the future, sign-in is temporarily locked for this account.</summary>
    public DateTime? LockoutEndsAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
