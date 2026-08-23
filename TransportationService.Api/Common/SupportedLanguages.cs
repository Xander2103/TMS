namespace TransportationService.Api.Common;

/// <summary>
/// De ene taalcatalogus van de applicatie-UI en gebruikerscommunicatie: nl / fr / en.
/// Elke plek die een taalcode valideert of normaliseert hoort hierlangs te lopen —
/// géén losse ["nl","fr","en"]-arrays meer per service. De tenant-Language-lookup
/// (Reference-module) blijft bestaan voor HR-master-data (daar mag bv. ook "de" in),
/// maar de UI-/communicatietaal van het product zelf is dit gesloten drietal.
/// Een nieuwe producttaal toevoegen = hier uitbreiden + vertaalbundels aanleveren.
/// </summary>
public static class SupportedLanguages
{
    public const string Nl = "nl";
    public const string Fr = "fr";
    public const string En = "en";

    public const string Default = Nl;

    public static readonly IReadOnlyList<string> All = [Nl, Fr, En];

    public static bool IsSupported(string? language) =>
        language is not null && All.Contains(language.Trim().ToLowerInvariant());

    /// <summary>Trim + lowercase; onbekende of lege waarden vallen terug op de default (nl).</summary>
    public static string Normalize(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        return normalized is Nl or Fr or En ? normalized : Default;
    }

    /// <summary>Zoals Normalize, maar zonder fallback: null wanneer de waarde geen producttaal is.</summary>
    public static string? NormalizeOrNull(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        return normalized is Nl or Fr or En ? normalized : null;
    }
}
