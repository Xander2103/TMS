using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// Normalisation + validation for person-level fields (IBAN, BIC, Belgian national
/// register number). Throws <see cref="DomainValidationException"/> with a Dutch message
/// on invalid input; empty input always normalises to null.
/// </summary>
public static class EmployeePersonValidators
{
    public static string? NormalizeIban(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var normalized = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (normalized.Length is < 15 or > 34
            || !char.IsAsciiLetter(normalized[0]) || !char.IsAsciiLetter(normalized[1])
            || !char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3])
            || !normalized.All(char.IsAsciiLetterOrDigit))
        {
            throw new DomainValidationException("IBAN heeft een ongeldig formaat.");
        }

        // Standard IBAN mod-97 check: move the first 4 chars to the end, letters → numbers.
        var rearranged = normalized[4..] + normalized[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value < 10
                ? (remainder * 10 + value) % 97
                : (remainder * 100 + value) % 97;
        }

        if (remainder != 1)
        {
            throw new DomainValidationException("IBAN heeft een ongeldig controlegetal.");
        }

        return normalized;
    }

    public static string? NormalizeBic(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var normalized = input.Trim().ToUpperInvariant();
        var valid = normalized.Length is 8 or 11
            && normalized[..6].All(char.IsAsciiLetter)
            && normalized[6..].All(char.IsAsciiLetterOrDigit);
        if (!valid)
        {
            throw new DomainValidationException("BIC moet uit 8 of 11 tekens bestaan (bv. KREDBEBB).");
        }

        return normalized;
    }

    public static string? NormalizeNationalRegisterNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 11)
        {
            throw new DomainValidationException("Rijksregisternummer moet uit 11 cijfers bestaan.");
        }

        // Belgian checksum: 97 - (first 9 digits % 97); people born in/after 2000 prefix a 2.
        var body = long.Parse(digits[..9]);
        var check = int.Parse(digits[9..]);
        var validPre2000 = check == 97 - (int)(body % 97);
        var validPost2000 = check == 97 - (int)((2000000000L + body) % 97);
        if (!validPre2000 && !validPost2000)
        {
            throw new DomainValidationException("Rijksregisternummer heeft een ongeldig controlegetal.");
        }

        return digits;
    }
}
