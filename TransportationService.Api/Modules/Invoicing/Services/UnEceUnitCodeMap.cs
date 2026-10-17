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
            // Packaging / colli — unit-type CODES of the Transport catalogue (PIECE, COLLI, EUROPALLET,
            // STANDARDPALLET, BLOCKPALLET, CONTAINER …) resolve here too (P7): the human unit stays
            // on the line as UnitLabel, the structured UN/ECE code is what the e-invoice carries.
            ["colli"] = "XPK", ["collo"] = "XPK", ["package"] = "XPK",
            ["stuk"] = "C62", ["stuks"] = "C62", ["st"] = "C62", ["piece"] = "C62", ["pieces"] = "C62",
            ["pallet"] = "XPX", ["europallet"] = "XPX", ["epal"] = "XPX", ["blokpallet"] = "XPX",
            ["standaardpallet"] = "XPX", ["standardpallet"] = "XPX", ["blockpallet"] = "XPX", ["halfpallet"] = "XPX",
            ["container"] = "CH", ["box"] = "BX", ["doos"] = "BX", ["zak"] = "BG", ["bag"] = "BG", ["rol"] = "RO", ["roll"] = "RO",
            // Weight
            ["kg"] = "KGM", ["kilogram"] = "KGM", ["ton"] = "TNE", ["t"] = "TNE",
            // Volume / capacity
            ["m3"] = "MTQ", ["liter"] = "LTR", ["l"] = "LTR",
            // Distance / loading meters
            ["ldm"] = "MTR", ["laadmeter"] = "MTR", ["m"] = "MTR", ["km"] = "KMT", ["kilometer"] = "KMT",
            // Time
            ["uur"] = "HUR", ["u"] = "HUR", ["hour"] = "HUR", ["hours"] = "HUR", ["dag"] = "DAY", ["day"] = "DAY",
            // Distance / weight / volume codes of the catalogue
            ["kilometer"] = "KMT", ["kilogram"] = "KGM", ["tonne"] = "TNE", ["loadingmeter"] = "MTR", ["loadingmeters"] = "MTR",
            ["cubicmeter"] = "MTQ", ["litre"] = "LTR",
        };

    private static readonly IReadOnlyDictionary<string, string> LabelByCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["C62"] = "stuk", ["XPX"] = "pallet", ["XPK"] = "colli", ["KGM"] = "kg", ["TNE"] = "ton", ["MTQ"] = "m³",
            ["MTR"] = "ldm", ["KMT"] = "km", ["HUR"] = "uur", ["DAY"] = "dag", ["LTR"] = "l", ["CH"] = "container",
            ["BX"] = "doos", ["BG"] = "zak", ["RO"] = "rol",
        };

    /// <summary>Human label for a structured code ("HUR" → "uur"); null for an unknown code or the default piece.</summary>
    public static string? LabelFor(string? unitCode) =>
        !string.IsNullOrWhiteSpace(unitCode) && LabelByCode.TryGetValue(unitCode.Trim(), out var label) && unitCode.Trim() != "C62"
            ? label
            : null;

    /// <summary>UN/ECE code for a tenant unit code or symbol; C62 when unknown or empty.</summary>
    public static string Resolve(string? unitTypeCodeOrSymbol)
    {
        if (string.IsNullOrWhiteSpace(unitTypeCodeOrSymbol))
        {
            return Default;
        }

        var key = unitTypeCodeOrSymbol.Trim();
        if (ByKnownKey.TryGetValue(key, out var mapped))
        {
            return mapped;
        }

        // A value that already IS a UN/ECE code ("HUR", "KGM") passes through unchanged.
        var upper = key.ToUpperInvariant();
        return LabelByCode.ContainsKey(upper) ? upper : Default;
    }
}
