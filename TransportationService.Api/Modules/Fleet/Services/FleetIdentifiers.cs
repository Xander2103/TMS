namespace TransportationService.Api.Modules.Fleet.Services;

/// <summary>
/// FL-M1: one place that decides when two plates / chassis numbers are "the same". The display
/// value keeps its formatting; identity (uniqueness, search) uses the normalised form.
/// </summary>
public static class FleetIdentifiers
{
    /// <summary>Upper-case letters and digits only: "1-tft-001", "1 TFT 001" and "1TFT001" are one plate.</summary>
    public static string NormalizePlate(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder(plate.Length);
        foreach (var ch in plate)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(char.ToUpperInvariant(ch));
            }
        }

        return buffer.ToString();
    }

    /// <summary>Display plate: trimmed, upper-case, inner whitespace collapsed (separators kept).</summary>
    public static string DisplayPlate(string plate) =>
        string.Join(' ', plate.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>VIN without whitespace, upper-case; null when empty.</summary>
    public static string? NormalizeVin(string? vin)
    {
        if (string.IsNullOrWhiteSpace(vin))
        {
            return null;
        }

        return string.Concat(vin.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }
}
