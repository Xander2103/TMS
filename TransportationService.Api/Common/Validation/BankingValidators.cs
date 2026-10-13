namespace TransportationService.Api.Common.Validation;

/// <summary>
/// Shared IBAN/BIC normalisation + validation (used by both the employee and customer
/// modules — one implementation, never two). Throws <see cref="DomainValidationException"/>
/// with a Dutch message; empty input normalises to null.
/// </summary>
public static class BankingValidators
{
    public static string? NormalizeIban(string? input, string field = "iban")
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
            throw new DomainValidationException(field, "IBAN heeft een ongeldig formaat.");
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
            throw new DomainValidationException(field, "IBAN heeft een ongeldig controlegetal.");
        }

        return normalized;
    }

    public static string? NormalizeBic(string? input, string field = "bic")
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
            throw new DomainValidationException(field, "BIC moet uit 8 of 11 tekens bestaan (bv. KREDBEBB).");
        }

        return normalized;
    }
}
