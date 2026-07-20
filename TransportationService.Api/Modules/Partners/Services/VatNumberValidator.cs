using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>
/// VAT number normalisation + validation. Belgian numbers ("BE" prefix) are checked
/// strictly (10 digits + mod-97 checksum); foreign numbers only get a loose sanity check
/// so legitimate formats from any country are never blocked.
/// </summary>
public static class VatNumberValidator
{
    /// <summary>
    /// Strips separators and uppercases; returns null for empty input.
    /// Throws <see cref="DomainValidationException"/> when the number is clearly invalid.
    /// The optional field path lets callers bind the error to their own request field.
    /// </summary>
    public static string? NormalizeAndValidate(string? input, string field = "vatNumber")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var normalized = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray())
            .ToUpperInvariant();

        if (normalized.StartsWith("BE", StringComparison.Ordinal))
        {
            ValidateBelgian(normalized, field);
            return normalized;
        }

        // Foreign VAT: loose sanity only (alphanumeric, plausible length).
        if (normalized.Length is < 4 or > 20 || !normalized.All(char.IsAsciiLetterOrDigit))
        {
            throw new DomainValidationException(field, "BTW-nummer heeft een ongeldig formaat.");
        }

        return normalized;
    }

    private static void ValidateBelgian(string normalized, string field)
    {
        // BE + 10 digits, first digit 0 or 1, and the last two digits are 97 - (first 8 % 97).
        var digits = normalized[2..];
        if (digits.Length != 10 || !digits.All(char.IsAsciiDigit))
        {
            throw new DomainValidationException(field, "Belgisch BTW-nummer moet uit BE + 10 cijfers bestaan (bv. BE0123456749).");
        }

        if (digits[0] is not ('0' or '1'))
        {
            throw new DomainValidationException(field, "Belgisch BTW-nummer moet met BE0 of BE1 beginnen.");
        }

        var body = long.Parse(digits[..8]);
        var check = int.Parse(digits[8..]);
        if (check != 97 - (int)(body % 97))
        {
            throw new DomainValidationException(field, "Belgisch BTW-nummer heeft een ongeldig controlegetal.");
        }
    }
}
