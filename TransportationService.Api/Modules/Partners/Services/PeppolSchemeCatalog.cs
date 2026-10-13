namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>One Peppol EAS scheme entry (subset relevant to this tenant base).</summary>
public sealed record PeppolSchemeInfo(string Code, string Label, string? CountryCode);

/// <summary>
/// Authoritative list of Peppol scheme (EAS) codes offered in the UI, mirroring the
/// single-source-of-truth pattern of <see cref="VatTreatmentCatalog"/>. Not exhaustive —
/// extend as tenants require. Codes are the 4-digit EAS identifiers.
/// </summary>
public static class PeppolSchemeCatalog
{
    public static IReadOnlyList<PeppolSchemeInfo> All { get; } = new List<PeppolSchemeInfo>
    {
        new("0208", "Belgisch ondernemingsnummer (KBO/BCE)", "BE"),
        new("9925", "Belgisch BTW-nummer", "BE"),
        new("0106", "Nederlands KvK-nummer", "NL"),
        new("9944", "Nederlands BTW-nummer", "NL"),
        new("0088", "GLN (EAN Location Code)", null),
        new("9930", "Duits BTW-nummer", "DE"),
        new("0009", "Frans SIRET", "FR"),
    };

    private static readonly HashSet<string> Codes =
        All.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string code) => Codes.Contains(code);

    /// <summary>Best-effort default scheme for a country; null when there is no single obvious choice.</summary>
    public static string? InferSchemeForCountry(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        return countryCode.Trim().ToUpperInvariant() switch
        {
            "BE" => "0208",
            "NL" => "0106",
            _ => null,
        };
    }
}
