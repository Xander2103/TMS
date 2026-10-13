using System.Text.Json;
using ClosedXML.Excel;

namespace TransportationService.Api.Modules.Tarification.Services;

/// <summary>
/// One column of the pricing-import sheet: the stable field key the importer uses, the header
/// the standard template writes, and whether a file without it can be read at all.
/// </summary>
public record PricingImportColumn(string Key, string StandardHeader, bool Required);

/// <summary>
/// Resolves "which spreadsheet column feeds which pricing field" (sprint 4D).
///
/// Without a profile the standard headers are matched BY NAME — the export writes exactly those
/// headers, so round-tripping keeps working, and a file with the columns in a different order
/// now imports correctly too. With a profile, the tenant's own header names are used, so a
/// customer's existing rate sheet can be imported as-is instead of being retyped.
/// </summary>
public static class PricingImportColumns
{
    /// <summary>
    /// Canonical fields, in template order. Only Naam is truly required: every other field has
    /// a defined meaning when absent (empty = not set), and blocking on them would reject
    /// perfectly usable customer sheets.
    /// </summary>
    public static readonly IReadOnlyList<PricingImportColumn> All =
    [
        new("regelId", "RegelId", false),
        new("naam", "Naam", true),
        new("basis", "Basis", false),
        new("eenheid", "Eenheid", false),
        new("zone", "Zone", false),
        new("prioriteit", "Prioriteit", false),
        new("staffelVan", "Staffel van", false),
        new("staffelTot", "Staffel tot", false),
        new("gewichtTot", "Gewicht tot (kg)", false),
        new("volumeTot", "Volume tot (m³)", false),
        new("laadmeterTot", "Laadmeter tot", false),
        new("staffelprijs", "Staffelprijs", false),
        new("prijsPerExtra", "Prijs per extra", false),
        new("eenheidsprijs", "Eenheidsprijs", false),
        new("basisbedrag", "Basisbedrag", false),
        new("minimum", "Minimum", false),
        new("maximum", "Maximum", false),
        new("minAantal", "Min. aantal", false),
        new("afrondingsstap", "Afrondingsstap", false),
        new("staffelmodus", "Staffelmodus", false),
        new("geldigVan", "Geldig van", false),
        new("geldigTot", "Geldig tot", false),
    ];

    /// <summary>Header text → column number, plus the headers that could not be matched.</summary>
    public record Resolution(IReadOnlyDictionary<string, int> ColumnByField, IReadOnlyList<string> MissingRequired);

    /// <summary>Comparison ignores case, surrounding space and non-breaking spaces from Excel.</summary>
    private static string Normalise(string? header) =>
        (header ?? string.Empty).Replace(' ', ' ').Trim().ToLowerInvariant();

    public static IReadOnlyDictionary<string, string> ParseMapping(string? mappingJson)
    {
        if (string.IsNullOrWhiteSpace(mappingJson)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // A corrupt profile must not take the whole import down; the standard headers still apply.
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Matches the sheet's header row against the standard headers, overridden per field by the
    /// profile mapping when one is supplied.
    /// </summary>
    public static Resolution Resolve(IXLWorksheet sheet, int headerRow, IReadOnlyDictionary<string, string> mapping)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var byHeader = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var column = 1; column <= lastColumn; column += 1)
        {
            var header = Normalise(sheet.Cell(headerRow, column).GetString());
            // First occurrence wins: a duplicated header should not silently shift the mapping.
            if (header.Length > 0 && !byHeader.ContainsKey(header)) byHeader[header] = column;
        }

        var columnByField = new Dictionary<string, int>(StringComparer.Ordinal);
        var missingRequired = new List<string>();

        foreach (var column in All)
        {
            var wanted = mapping.TryGetValue(column.Key, out var custom) && !string.IsNullOrWhiteSpace(custom)
                ? custom
                : column.StandardHeader;

            if (byHeader.TryGetValue(Normalise(wanted), out var index))
            {
                columnByField[column.Key] = index;
            }
            else if (column.Required)
            {
                missingRequired.Add(wanted);
            }
        }

        return new Resolution(columnByField, missingRequired);
    }

    /// <summary>The header texts actually present in the sheet — offered to the user when mapping.</summary>
    public static IReadOnlyList<string> ReadHeaders(IXLWorksheet sheet, int headerRow)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var headers = new List<string>();
        for (var column = 1; column <= lastColumn; column += 1)
        {
            var header = sheet.Cell(headerRow, column).GetString().Replace(' ', ' ').Trim();
            if (header.Length > 0) headers.Add(header);
        }

        return headers;
    }
}
