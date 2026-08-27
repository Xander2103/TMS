using System.Globalization;
using System.Text;

namespace TransportationService.Api.Modules.Locations.Services;

/// <summary>
/// Turns the physical address fields into stable comparison keys (sprint 2, duplicate
/// detection). Matching is done on the NORMALISED physical fields — never on the formatted
/// display string, which differs per caller and would both miss real duplicates and invent
/// false ones.
///
/// Two tiers:
/// <list type="bullet">
/// <item><see cref="ExactKey"/> — country + postcode + city + street + house number. Same key
/// means the same front door; creating another one requires an explicit override.</item>
/// <item><see cref="StreetKey"/> — the same without the house number. Same key means the same
/// street, which is worth SHOWING as a candidate but is a normal, allowed situation
/// (house number 10 and 12 are different addresses).</item>
/// </list>
/// </summary>
public static class AddressNormalizer
{
    /// <summary>Strips diacritics and casing so "Sint-Niklaas" and "sint niklaas" compare equal.</summary>
    private static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                // Any run of whitespace/punctuation collapses to a single space, so
                // "Noorderlaan  10", "Noorderlaan-10" and "noorderlaan 10" agree.
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Postcode/house number: separators carry no meaning ("2030", "B-2030", "10 A" → "10a").</summary>
    private static string FoldCompact(string? value)
    {
        var folded = Fold(value);
        return folded.Length == 0 ? string.Empty : folded.Replace(" ", string.Empty);
    }

    private static string FoldCountry(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    /// <summary>
    /// The same front door: country|postcode|city|street|houseNumber. Empty when there is not
    /// enough address to compare — callers must treat an empty key as "no duplicate check
    /// possible" rather than as a match against other empty keys.
    /// </summary>
    public static string ExactKey(string? countryCode, string? postalCode, string? city, string? street, string? houseNumber)
    {
        var streetKey = StreetKey(countryCode, postalCode, city, street);
        return streetKey.Length == 0 ? string.Empty : $"{streetKey}|{FoldCompact(houseNumber)}";
    }

    /// <summary>The same street, ignoring the house number. Empty when street and city are both blank.</summary>
    public static string StreetKey(string? countryCode, string? postalCode, string? city, string? street)
    {
        var streetPart = Fold(street);
        var cityPart = Fold(city);
        if (streetPart.Length == 0 && cityPart.Length == 0) return string.Empty;

        return string.Join('|', FoldCountry(countryCode), FoldCompact(postalCode), cityPart, streetPart);
    }
}
