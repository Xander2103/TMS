using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
/// Everything here is deterministic and invariant-culture: the keys are persisted and compared
/// across processes, so they must never depend on the machine's locale.
/// </summary>
public static partial class AddressNormalizer
{
    /// <summary>Lowercases, folds diacritics (and ß → ss) and strips the surrounding whitespace; separators are kept.</summary>
    private static string FoldChars(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Replace("ß", "ss").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Strips diacritics and casing so "Sint-Niklaas" and "sint niklaas" compare equal.</summary>
    private static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var folded = FoldChars(value);
        var builder = new StringBuilder(folded.Length);
        var lastWasSeparator = false;

        foreach (var ch in folded)
        {
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

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Postcode: separators carry no meaning ("1234 AB" == "1234ab") and a leading 1–2 letter
    /// ISO prefix with a dash is dropped ("B-2030" == "2030", "NL-1234 AB" == "1234ab").
    /// </summary>
    private static string FoldPostalCode(string? value)
    {
        var folded = Fold(value);
        if (folded.Length == 0) return string.Empty;

        // Fold() turned the dash into a space, so the prefix is "b " / "nl " here.
        var withoutPrefix = IsoPrefix().Replace(folded, string.Empty);
        return withoutPrefix.Replace(" ", string.Empty);
    }

    /// <summary>
    /// House number: letters are lowercased and kept adjacent ("10 A" == "10a"), while the
    /// bus/box separators are canonicalised to a single "/" instead of being deleted —
    /// "1 bus 1", "1/1", "1-1" and "1 b 1" all become "1/1", which stays distinct from "11".
    /// </summary>
    private static string FoldHouseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var folded = FoldChars(value);
        // Explicit separators first, so "1 / 1" and "1-1" become "1/1".
        folded = PunctuationSeparator().Replace(folded, "/");
        // Word separators between two alphanumeric parts: "1 bus 1", "10a bte 2", "1 b 1", "1b1".
        folded = WordSeparator().Replace(folded, "/");
        folded = SingleLetterSeparator().Replace(folded, "/");
        // Whatever is left over that is neither alphanumeric nor "/" carries no meaning.
        folded = Residue().Replace(folded, string.Empty);
        folded = RepeatedSlash().Replace(folded, "/");
        return folded.Trim('/');
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
        return streetKey.Length == 0 ? string.Empty : $"{streetKey}|{FoldHouseNumber(houseNumber)}";
    }

    /// <summary>
    /// The same street, ignoring the house number. Empty when the street is blank: a city-only
    /// record is not "the same street" as every address in that city.
    /// </summary>
    public static string StreetKey(string? countryCode, string? postalCode, string? city, string? street)
    {
        var streetPart = Fold(street);
        if (streetPart.Length == 0) return string.Empty;

        return string.Join('|', FoldCountry(countryCode), FoldPostalCode(postalCode), Fold(city), streetPart);
    }

    [GeneratedRegex(@"^[a-z]{1,2} (?=[a-z0-9])")]
    private static partial Regex IsoPrefix();

    [GeneratedRegex(@"\s*[/\-\\]\s*")]
    private static partial Regex PunctuationSeparator();

    [GeneratedRegex(@"(?<=[a-z0-9])\s*(?:bus|box|bte|boite)\s*(?=[a-z0-9])")]
    private static partial Regex WordSeparator();

    [GeneratedRegex(@"(?<=[0-9])\s*b\s*(?=[0-9])")]
    private static partial Regex SingleLetterSeparator();

    [GeneratedRegex(@"[^a-z0-9/]")]
    private static partial Regex Residue();

    [GeneratedRegex(@"/{2,}")]
    private static partial Regex RepeatedSlash();
}
