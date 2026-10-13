using System.ComponentModel.DataAnnotations;

namespace TransportationService.Api.Modules.Authentication;

/// <summary>
/// Single source of truth for password rules. Deliberately length-first: no forced character-class
/// rules (which push users toward predictable "P@ssw0rd!" shapes), but a real minimum length plus
/// an offline deny-list of well-known/breached passwords. Nothing is ever sent to an external
/// service — the check is a local, case-insensitive comparison.
/// </summary>
public sealed class PasswordPolicyOptions
{
    public const string SectionName = "Security:PasswordPolicy";

    [Range(8, 256)]
    public int MinimumLength { get; set; } = 12;

    /// <summary>Extra tenant-specific words to refuse (product name, company name, ...).</summary>
    public string[] AdditionalDeniedPasswords { get; set; } = [];
}

public interface IPasswordPolicy
{
    int MinimumLength { get; }

    /// <summary>Null when acceptable; otherwise a Dutch, actionable reason.</summary>
    string? Validate(string? password);
}

public sealed class PasswordPolicy : IPasswordPolicy
{
    // Small, high-signal offline list: the passwords that dominate credential-stuffing lists,
    // plus obvious product-specific guesses. Compared case-insensitively.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passw0rd", "wachtwoord", "wachtwoord1",
        "welkom", "welkom01", "welkom123", "welcome", "welcome1", "welcome123",
        "123456", "1234567", "12345678", "123456789", "1234567890", "qwerty", "qwerty123",
        "azerty", "azerty123", "letmein", "iloveyou", "admin", "administrator", "admin123",
        "changeme", "geheim", "geheim123", "test1234", "transport", "transport123",
        "abc12345", "qwertyuiop", "zaq12wsx", "monkey", "dragon", "sunshine", "princess",
        "football", "baseball", "starwars", "whatever", "trustno1", "master", "shadow",
    };

    /// <summary>
    /// Policy with the built-in defaults, for call sites that are not (yet) DI-composed. Keeps the
    /// rules identical everywhere — there is exactly one implementation of the rule set.
    /// </summary>
    public static readonly PasswordPolicy Default =
        new(Microsoft.Extensions.Options.Options.Create(new PasswordPolicyOptions()));

    private readonly PasswordPolicyOptions _options;

    public PasswordPolicy(Microsoft.Extensions.Options.IOptions<PasswordPolicyOptions> options)
    {
        _options = options.Value;
    }

    public int MinimumLength => _options.MinimumLength;

    public string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Een wachtwoord is verplicht.";
        }

        if (password.Length < _options.MinimumLength)
        {
            return $"Het wachtwoord moet minstens {_options.MinimumLength} tekens lang zijn.";
        }

        var trimmed = password.Trim();
        if (CommonPasswords.Contains(trimmed)
            || _options.AdditionalDeniedPasswords.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return "Dit wachtwoord komt voor in bekende wachtwoordenlijsten; kies een uniek wachtwoord.";
        }

        // A single repeated character (aaaaaaaaaaaa) technically passes the length rule.
        if (trimmed.Distinct().Count() <= 3)
        {
            return "Het wachtwoord is te eenvoudig; gebruik meer verschillende tekens.";
        }

        return null;
    }
}
