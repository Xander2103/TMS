namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// Best-effort mapping from tenant unit-type codes/symbols onto UN/ECE Recommendation 20
/// codes for Peppol invoice lines. Unit types are tenant-editable free data, so this maps by
/// well-known code and symbol; anything unknown falls back to C62 ("one/stuk") — the BIS 3.0
/// safe default. <see cref="Selectable"/> is the ready-made option list for a future invoice
/// line unit select (not rendered yet). Structural line codes (transport C62, per-hour
/// service HUR) are set directly by InvoiceService from domain knowledge.
/// </summary>
public static class UnEceUnitCodeMap
{
    public const string Default = "C62";

    /// <summary>Common selectable codes with Dutch labels (for the invoice line unit select).</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Selectable =
    [
        ("C62", "Stuk"),
        ("XPX", "Pallet"),
        ("KGM", "Kilogram"),
        ("TNE", "Ton"),
        ("MTQ", "Kubieke meter"),
        ("MTR", "Meter (laadmeter)"),
        ("KMT", "Kilometer"),
        ("HUR", "Uur"),
        ("DAY", "Dag"),
        ("LTR", "Liter"),
    ];

    private static readonly IReadOnlyDictionary<string, string> ByKnownKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Packaging / colli
            ["colli"] = "C62", ["stuk"] = "C62", ["stuks"] = "C62", ["st"] = "C62", ["piece"] = "C62",
            ["pallet"] = "XPX", ["europallet"] = "XPX", ["epal"] = "XPX", ["blokpallet"] = "XPX",
            // Weight
            ["kg"] = "KGM", ["kilogram"] = "KGM", ["ton"] = "TNE", ["t"] = "TNE",
            // Volume / capacity
            ["m3"] = "MTQ", ["liter"] = "LTR", ["l"] = "LTR",
            // Distance / loading meters
            ["ldm"] = "MTR", ["laadmeter"] = "MTR", ["m"] = "MTR", ["km"] = "KMT", ["kilometer"] = "KMT",
            // Time
            ["uur"] = "HUR", ["u"] = "HUR", ["hour"] = "HUR", ["dag"] = "DAY", ["day"] = "DAY",
        };

    /// <summary>UN/ECE code for a tenant unit code or symbol; C62 when unknown or empty.</summary>
    public static string Resolve(string? unitTypeCodeOrSymbol) =>
        !string.IsNullOrWhiteSpace(unitTypeCodeOrSymbol)
        && ByKnownKey.TryGetValue(unitTypeCodeOrSymbol.Trim(), out var mapped)
            ? mapped
            : Default;
}
