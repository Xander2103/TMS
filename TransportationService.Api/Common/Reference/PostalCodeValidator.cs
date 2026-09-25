using System.Text.RegularExpressions;

namespace TransportationService.Api.Common.Reference;

/// <summary>
/// D3: postal-code FORMAT check per country (BE/NL/FR/DE/LU). Pure and deterministic — it never
/// looks a code up, it only refuses a shape that cannot exist in that country ("30800" for
/// Belgium). Every other country, and an empty postal code, passes: the address master is
/// international and an unknown format is no reason to block a save.
/// </summary>
/// <remarks>
/// Callers apply it to NEW or CHANGED values only, so stored legacy data never blocks an edit. A
/// leading ISO prefix with a dash ("B-2030", "NL-1234 AB") is tolerated, exactly as
/// <c>AddressNormalizer</c> treats it as the same postal code.
/// </remarks>
public static partial class PostalCodeValidator
{
    /// <summary>Dutch error message, or null when the postal code is acceptable for the country.</summary>
    public static string? Validate(string? countryCode, string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var code = IsoPrefix().Replace(postalCode.Trim(), string.Empty);
        return countryCode.Trim().ToUpperInvariant() switch
        {
            "BE" => FourDigits().IsMatch(code) ? null : "Een Belgische postcode heeft 4 cijfers.",
            "NL" => Dutch().IsMatch(code) ? null : "Een Nederlandse postcode heeft 4 cijfers en 2 letters (bv. 1234 AB).",
            "FR" => FiveDigits().IsMatch(code) ? null : "Een Franse postcode heeft 5 cijfers.",
            "DE" => FiveDigits().IsMatch(code) ? null : "Een Duitse postcode heeft 5 cijfers.",
            "LU" => FourDigits().IsMatch(code) ? null : "Een Luxemburgse postcode heeft 4 cijfers.",
            _ => null,
        };
    }

    /// <summary>Throws a field-bound validation error when <see cref="Validate"/> refuses the value.</summary>
    public static void EnsureValid(string? countryCode, string? postalCode, string field)
    {
        if (Validate(countryCode, postalCode) is { } error)
        {
            throw new DomainValidationException(field, error);
        }
    }

    [GeneratedRegex(@"^[A-Za-z]{1,2}\s*-\s*")]
    private static partial Regex IsoPrefix();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex FourDigits();

    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex FiveDigits();

    [GeneratedRegex(@"^\d{4}\s?[A-Za-z]{2}$")]
    private static partial Regex Dutch();
}
