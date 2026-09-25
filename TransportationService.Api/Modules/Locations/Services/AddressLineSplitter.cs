using System.Text.RegularExpressions;

namespace TransportationService.Api.Modules.Locations.Services;

/// <summary>
/// D3: an order stop keeps its address as ONE line ("Noorderlaan 10 bus 3"), the address master
/// stores street and house number separately. This splits the line on a TRAILING house number
/// (digits, optional letter, optional bus/box suffix). Anything it cannot split with confidence —
/// no trailing number, a leading number ("10 Downing Street"), an over-long number — stays whole
/// in the street: a wrong split would corrupt the duplicate keys, a missing one only weakens them.
/// </summary>
public static partial class AddressLineSplitter
{
    private const int MaxHouseNumberLength = 20;

    public static (string? Street, string? HouseNumber) Split(string? addressLine)
    {
        if (string.IsNullOrWhiteSpace(addressLine))
        {
            return (null, null);
        }

        var line = Whitespace().Replace(addressLine.Trim(), " ");
        var match = TrailingHouseNumber().Match(line);
        if (!match.Success)
        {
            return (line, null);
        }

        var street = match.Groups["street"].Value.Trim().TrimEnd(',').TrimEnd();
        var houseNumber = match.Groups["number"].Value.Trim();
        if (street.Length == 0 || houseNumber.Length > MaxHouseNumberLength)
        {
            return (line, null);
        }

        return (street, houseNumber);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // street (lazy, must end in a non-digit) + separator + number [letter] [bus/box/"/"/"-" suffix].
    [GeneratedRegex(
        @"^(?<street>.*?[^\d\s,])[\s,]+(?<number>\d+\s?[a-z]?(?:\s*(?:[-/]|bus|bte|box|bo[iî]te|b)\s*[a-z0-9]+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingHouseNumber();
}
