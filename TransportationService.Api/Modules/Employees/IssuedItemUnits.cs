namespace TransportationService.Api.Modules.Employees;

/// <summary>
/// Fixed catalogue of units for issued-item templates ("bedrijfsmiddelen"). Deliberately NOT
/// the general UnitType master data (that table serves transport/pricing): company assets need
/// a small, stable list that the frontend mirrors one-to-one (<c>ISSUED_ITEM_UNITS</c>).
/// Stored value = the stable code; labels are translated client-side.
/// </summary>
public static class IssuedItemUnits
{
    public const string Piece = "piece";
    public const string Pair = "pair";
    public const string Set = "set";
    public const string Box = "box";
    public const string Pack = "pack";
    public const string Roll = "roll";
    public const string Meter = "meter";
    public const string Liter = "liter";
    public const string Kilogram = "kilogram";
    public const string Other = "other";

    public const string Default = Piece;

    /// <summary>Catalogue order as shown in the UI (Stuk first, Overige last).</summary>
    public static readonly IReadOnlyList<string> Codes =
        [Piece, Pair, Set, Box, Pack, Roll, Meter, Liter, Kilogram, Other];

    private static readonly HashSet<string> CodeSet = new(Codes, StringComparer.Ordinal);

    /// <summary>
    /// Legacy free-text/master-data names that existed before the catalogue (Dutch labels and
    /// UnitType codes such as "Stuks", "Paar", "KG"). Used by the data migration and, defensively,
    /// when reading rows that predate it.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stuk"] = Piece, ["stuks"] = Piece, ["st"] = Piece, ["piece"] = Piece, ["pieces"] = Piece, ["pcs"] = Piece,
        ["paar"] = Pair, ["pair"] = Pair, ["paire"] = Pair,
        ["set"] = Set, ["sets"] = Set,
        ["doos"] = Box, ["dozen"] = Box, ["box"] = Box, ["boxes"] = Box, ["boîte"] = Box,
        ["pak"] = Pack, ["pakken"] = Pack, ["pack"] = Pack, ["paquet"] = Pack,
        ["rol"] = Roll, ["rollen"] = Roll, ["roll"] = Roll, ["rouleau"] = Roll,
        ["meter"] = Meter, ["m"] = Meter, ["mètre"] = Meter, ["metre"] = Meter,
        ["liter"] = Liter, ["l"] = Liter, ["litre"] = Liter,
        ["kilogram"] = Kilogram, ["kg"] = Kilogram, ["kilo"] = Kilogram,
        ["overige"] = Other, ["other"] = Other, ["andere"] = Other, ["autre"] = Other,
    };

    public static bool IsValid(string? code) => code is not null && CodeSet.Contains(code);

    /// <summary>
    /// Maps an incoming value to a catalogue code: null/blank → the default (Stuk), a code →
    /// itself, a known legacy name → its code. Anything else is rejected (returns null).
    /// </summary>
    public static string? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        var trimmed = value.Trim();
        var lower = trimmed.ToLowerInvariant();
        if (CodeSet.Contains(lower))
        {
            return lower;
        }

        return LegacyAliases.TryGetValue(trimmed, out var mapped) ? mapped : null;
    }

    /// <summary>Read-side normalisation: unknown stored values surface as "other" instead of leaking free text.</summary>
    public static string Normalize(string? storedValue) => TryParse(storedValue) ?? Other;
}
