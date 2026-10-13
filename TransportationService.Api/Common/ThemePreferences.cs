namespace TransportationService.Api.Common;

/// <summary>
/// De ene catalogus van de persoonlijke weergavevoorkeur (licht / donker / systeem).
/// Hoort bij de GEBRUIKER, nooit bij tenant, bedrijf, rol of werkplek: het is pure
/// presentatie en beïnvloedt nooit rechten of bedrijfslogica. Opslag als korte
/// tekstcode (zoals <see cref="SupportedLanguages"/>) zodat de frontend dezelfde
/// waarden letterlijk kan cachen en toepassen; null in de database = nog niet gekozen,
/// wat zich gedraagt als "systeem".
/// </summary>
public static class ThemePreferences
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public const string Default = System;

    public static readonly IReadOnlyList<string> All = [System, Light, Dark];

    public static bool IsSupported(string? theme) =>
        theme is not null && All.Contains(theme.Trim().ToLowerInvariant());

    /// <summary>Trim + lowercase; onbekende of lege waarden vallen terug op "system".</summary>
    public static string Normalize(string? theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant();
        return normalized is System or Light or Dark ? normalized : Default;
    }

    /// <summary>Zoals Normalize, maar zonder fallback: null wanneer de waarde geen geldige voorkeur is.</summary>
    public static string? NormalizeOrNull(string? theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant();
        return normalized is System or Light or Dark ? normalized : null;
    }
}
