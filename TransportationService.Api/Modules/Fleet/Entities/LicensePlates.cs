namespace TransportationService.Api.Modules.Fleet.Entities;

/// <summary>
/// FL-M1: the identity of a licence plate is its letters and digits, upper-cased — "1-TFT-001",
/// "1 tft 001" and "1TFT001" are the same physical plate. Display formatting is kept separately.
/// </summary>
public static class LicensePlates
{
    public static string Normalize(string? plate) =>
        new((plate ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
