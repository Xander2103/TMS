using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

public enum ImportRowAction
{
    Create,
    Update,
    Error,
}

public sealed record ImportRowResult(
    int RowNumber,
    ImportRowAction Action,
    string? PackageNumber,
    string Description,
    IReadOnlyList<string> Messages);

public sealed record ImportPreviewDto(
    int TotalRows,
    int Creates,
    int Updates,
    int Errors,
    IReadOnlyList<ImportRowResult> Rows);

public sealed record ImportCommitDto(
    int Created,
    int Updated,
    int Failed,
    bool Committed,
    IReadOnlyList<ImportRowResult> Rows,
    string? ErrorWorkbookBase64);

public interface IPackageImportService
{
    byte[] BuildTemplate();

    Task<(ImportPreviewDto? Preview, string? Error)> PreviewAsync(
        Guid transportOrderId, Stream workbook, CancellationToken cancellationToken);

    /// <summary>
    /// allOrNothing: any invalid row aborts the whole import. Otherwise valid rows commit and
    /// failures are reported per row plus as a downloadable error workbook. allowUpdates
    /// permits matching EXISTING packages by their internal PackageNumber only �?" ambiguous
    /// rows are errors, never silent merges.
    /// </summary>
    Task<(ImportCommitDto? Result, string? Error)> CommitAsync(
        Guid transportOrderId, Stream workbook, bool allOrNothing, bool allowUpdates,
        CancellationToken cancellationToken);
}

public class PackageImportService : IPackageImportService
{
    private const int MaxRows = 1000;

    private static readonly string[] Headers =
    [
        "PackageNumber (leeg = nieuw)", "Omschrijving*", "Aantal", "Eenheid", "ExterneBarcode",
        "ExterneReferentie", "Klantreferentie", "GewichtKg", "VolumeM3", "LengteCm", "BreedteCm",
        "HoogteCm", "Verplicht (ja/nee)", "Breekbaar (ja/nee)", "Temperatuur (ja/nee)",
        "Handtekening (ja/nee)", "Notities",
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IPackageBarcodeService _barcodeService;
    private readonly IPackageEventWriter _eventWriter;

    public PackageImportService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IPackageBarcodeService barcodeService,
        IPackageEventWriter eventWriter)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _barcodeService = barcodeService;
        _eventWriter = eventWriter;
    }

    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Colli");
        for (var column = 0; column < Headers.Length; column += 1)
        {
            sheet.Cell(1, column + 1).SetValue(Headers[column]).Style.Font.Bold = true;
        }

        // One example row (text values only �?" never formulas).
        sheet.Cell(2, 2).SetValue("Doos onderdelen");
        sheet.Cell(2, 3).SetValue(1);
        sheet.Cell(2, 4).SetValue("Colli");
        sheet.Cell(2, 8).SetValue(12.5);
        sheet.Cell(2, 13).SetValue("ja");
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        var help = workbook.AddWorksheet("Uitleg");
        var lines = new[]
        {
            "Kolommen met * zijn verplicht.",
            "PackageNumber alleen invullen om een BESTAAND collo bij te werken (interne nummers, bv. PKG-00012).",
            "Eenheid: Package, Parcel, Colli, Pallet, Crate, RollContainer, Container, Document of Other.",
            "Ja/nee-kolommen accepteren: ja/nee, true/false, 1/0.",
            "Externe barcodes moeten uniek zijn binnen het bedrijf; dubbels worden geweigerd, nooit stil samengevoegd.",
            $"Maximaal {MaxRows} rijen per import.",
        };
        for (var index = 0; index < lines.Length; index += 1)
        {
            help.Cell(index + 1, 1).SetValue(lines[index]);
        }

        help.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<(ImportPreviewDto? Preview, string? Error)> PreviewAsync(
        Guid transportOrderId, Stream workbook, CancellationToken cancellationToken)
    {
        var parsed = await ParseAndValidateAsync(transportOrderId, workbook, allowUpdates: true, cancellationToken);
        if (parsed.Error is not null)
        {
            return (null, parsed.Error);
        }

        var rows = parsed.Rows.Select(r => r.Result).ToList();
        return (new ImportPreviewDto(
            rows.Count,
            rows.Count(r => r.Action == ImportRowAction.Create),
            rows.Count(r => r.Action == ImportRowAction.Update),
            rows.Count(r => r.Action == ImportRowAction.Error),
            rows), null);
    }

    public async Task<(ImportCommitDto? Result, string? Error)> CommitAsync(
        Guid transportOrderId, Stream workbook, bool allOrNothing, bool allowUpdates,
        CancellationToken cancellationToken)
    {
        var parsed = await ParseAndValidateAsync(transportOrderId, workbook, allowUpdates, cancellationToken);
        if (parsed.Error is not null)
        {
            return (null, parsed.Error);
        }

        var rows = parsed.Rows;
        var errorRows = rows.Where(r => r.Result.Action == ImportRowAction.Error).ToList();
        if (allOrNothing && errorRows.Count > 0)
        {
            return (new ImportCommitDto(0, 0, errorRows.Count, Committed: false,
                rows.Select(r => r.Result).ToList(), BuildErrorWorkbook(errorRows)), null);
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var creations = new List<(ParsedRow Row, Package Package)>();
        foreach (var row in rows.Where(r => r.Result.Action == ImportRowAction.Create))
        {
            var package = new Package
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TransportOrderId = transportOrderId,
                Description = row.Description!,
                Quantity = row.Quantity ?? 1m,
                UnitType = row.UnitType ?? PackageUnitType.Colli,
                ExternalBarcode = row.ExternalBarcode,
                ExternalPackageReference = row.ExternalReference,
                CustomerReference = row.CustomerReference,
                WeightKg = row.WeightKg,
                VolumeM3 = row.VolumeM3,
                LengthCm = row.LengthCm,
                WidthCm = row.WidthCm,
                HeightCm = row.HeightCm,
                IsMandatory = row.IsMandatory ?? true,
                IsFragile = row.IsFragile ?? false,
                RequiresTemperatureControl = row.RequiresTemperature ?? false,
                RequiresSignature = row.RequiresSignature ?? false,
                Notes = row.Notes,
            };
            _dbContext.Add(package);
            creations.Add((row, package));
        }

        var updates = new List<(ParsedRow Row, Package Package)>();
        foreach (var row in rows.Where(r => r.Result.Action == ImportRowAction.Update))
        {
            var package = parsed.ExistingByNumber[row.PackageNumber!];
            package.Description = row.Description!;
            if (row.Quantity is { } quantity) package.Quantity = quantity;
            if (row.UnitType is { } unitType) package.UnitType = unitType;
            if (row.ExternalReference is not null) package.ExternalPackageReference = row.ExternalReference;
            if (row.CustomerReference is not null) package.CustomerReference = row.CustomerReference;
            if (row.WeightKg is not null) package.WeightKg = row.WeightKg;
            if (row.VolumeM3 is not null) package.VolumeM3 = row.VolumeM3;
            if (row.LengthCm is not null) package.LengthCm = row.LengthCm;
            if (row.WidthCm is not null) package.WidthCm = row.WidthCm;
            if (row.HeightCm is not null) package.HeightCm = row.HeightCm;
            if (row.IsMandatory is { } mandatory) package.IsMandatory = mandatory;
            if (row.IsFragile is { } fragile) package.IsFragile = fragile;
            if (row.RequiresTemperature is { } temperature) package.RequiresTemperatureControl = temperature;
            if (row.RequiresSignature is { } signature) package.RequiresSignature = signature;
            if (row.Notes is not null) package.Notes = row.Notes;
            updates.Add((row, package));
        }

        if (creations.Count > 0)
        {
            var barcodeRows = new Dictionary<Guid, PackageBarcode>();
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () =>
                {
                    foreach (var (row, package) in creations)
                    {
                        package.PackageNumber = GenerateNumber(settings);
                        package.BarcodeValue = _barcodeService.GenerateInternalValue(package.PackageNumber);
                        if (barcodeRows.TryGetValue(package.Id, out var existing))
                        {
                            existing.Value = package.BarcodeValue;
                        }
                        else
                        {
                            _barcodeService.StageInitialBarcodes(package, row.ExternalBarcode);
                            barcodeRows[package.Id] = _dbContext.ChangeTracker.Entries<PackageBarcode>()
                                .Select(e => e.Entity)
                                .First(b => b.PackageId == package.Id && b.Type != PackageBarcodeType.External);
                        }
                    }
                },
                cancellationToken);
        }

        foreach (var (row, package) in creations)
        {
            _eventWriter.Stage(package, PackageEventType.Created, null, PackageLifecycleStatus.Created,
                new PackageEventContext(OrderId: transportOrderId, BarcodeUsed: package.BarcodeValue,
                    Notes: $"Excel-import rij {row.Result.RowNumber}"));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("Package", transportOrderId.ToString(), "Imported", null,
            new { Created = creations.Count, Updated = updates.Count, Failed = errorRows.Count }, cancellationToken);

        return (new ImportCommitDto(
            creations.Count, updates.Count, errorRows.Count, Committed: true,
            rows.Select(r => r.Result).ToList(),
            errorRows.Count > 0 ? BuildErrorWorkbook(errorRows) : null), null);
    }

    // ------------------------------------------------------------- parsing

    private sealed record ParsedRow(
        ImportRowResult Result,
        string? PackageNumber,
        string? Description,
        decimal? Quantity,
        PackageUnitType? UnitType,
        string? ExternalBarcode,
        string? ExternalReference,
        string? CustomerReference,
        decimal? WeightKg,
        decimal? VolumeM3,
        decimal? LengthCm,
        decimal? WidthCm,
        decimal? HeightCm,
        bool? IsMandatory,
        bool? IsFragile,
        bool? RequiresTemperature,
        bool? RequiresSignature,
        string? Notes,
        IReadOnlyList<string> RawCells);

    private sealed record ParseOutcome(
        List<ParsedRow> Rows, Dictionary<string, Package> ExistingByNumber, string? Error);

    private async Task<ParseOutcome> ParseAndValidateAsync(
        Guid transportOrderId, Stream stream, bool allowUpdates, CancellationToken cancellationToken)
    {
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == transportOrderId && o.TenantId == _tenantContext.TenantId, cancellationToken);
        if (order is null)
        {
            return new ParseOutcome([], [], "De opdracht bestaat niet.");
        }

        if (order.Status is TransportOrderStatus.Cancelled)
        {
            return new ParseOutcome([], [], "Op een geannuleerde opdracht kan niet worden geïmporteerd.");
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch
        {
            return new ParseOutcome([], [], "Het bestand is geen geldig Excel-werkboek (.xlsx).");
        }

        using var _ = workbook;
        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            return new ParseOutcome([], [], "Het werkboek bevat geen werkblad.");
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (lastRow - 1 > MaxRows)
        {
            return new ParseOutcome([], [], $"Maximaal {MaxRows} rijen per import; dit bestand bevat er {lastRow - 1}.");
        }

        var existingPackages = await _dbContext.Packages
            .Where(p => p.TenantId == _tenantContext.TenantId && p.TransportOrderId == transportOrderId)
            .ToListAsync(cancellationToken);
        var existingByNumber = existingPackages.ToDictionary(p => p.PackageNumber, StringComparer.OrdinalIgnoreCase);
        var knownBarcodes = (await _dbContext.PackageBarcodes.AsNoTracking()
                .Where(b => b.TenantId == _tenantContext.TenantId)
                .Select(b => b.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<ParsedRow>();
        var seenBarcodesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNumbersInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenReferencesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber += 1)
        {
            var row = sheet.Row(rowNumber);
            if (row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString())))
            {
                continue;
            }

            string Cell(int column) => row.Cell(column).GetString().Trim();
            var raw = Enumerable.Range(1, Headers.Length).Select(Cell).ToList();
            var messages = new List<string>();

            var packageNumber = NullIfEmpty(Cell(1));
            var description = NullIfEmpty(Cell(2));
            var quantity = ParseDecimal(Cell(3), "Aantal", messages);
            var unitType = ParseUnit(Cell(4), messages);
            var externalBarcode = NullIfEmpty(Cell(5));
            var externalReference = NullIfEmpty(Cell(6));
            var customerReference = NullIfEmpty(Cell(7));
            var weight = ParseDecimal(Cell(8), "GewichtKg", messages);
            var volume = ParseDecimal(Cell(9), "VolumeM3", messages);
            var length = ParseDecimal(Cell(10), "LengteCm", messages);
            var width = ParseDecimal(Cell(11), "BreedteCm", messages);
            var height = ParseDecimal(Cell(12), "HoogteCm", messages);
            var mandatory = ParseBool(Cell(13), "Verplicht", messages);
            var fragile = ParseBool(Cell(14), "Breekbaar", messages);
            var temperature = ParseBool(Cell(15), "Temperatuur", messages);
            var signature = ParseBool(Cell(16), "Handtekening", messages);
            var notes = NullIfEmpty(Cell(17));

            var action = ImportRowAction.Create;
            if (packageNumber is not null)
            {
                if (!allowUpdates)
                {
                    messages.Add("Bijwerken is uitgeschakeld voor deze import.");
                }
                else if (!existingByNumber.TryGetValue(packageNumber, out var existing))
                {
                    messages.Add($"Onbekend collonummer '{packageNumber}' op deze opdracht.");
                }
                else if (PackageLifecycleMachine.IsTerminal(existing.CurrentLifecycleStatus))
                {
                    messages.Add($"Collo '{packageNumber}' is afgesloten en kan niet worden bijgewerkt.");
                }
                else if (!seenNumbersInFile.Add(packageNumber))
                {
                    messages.Add($"Collonummer '{packageNumber}' staat meermaals in het bestand.");
                }
                else
                {
                    action = ImportRowAction.Update;
                }
            }

            if (description is null)
            {
                messages.Add("Omschrijving is verplicht.");
            }

            if (quantity is <= 0)
            {
                messages.Add("Aantal moet groter dan nul zijn.");
            }

            if (externalBarcode is not null)
            {
                if (_barcodeService.ValidateExternalValue(externalBarcode) is { } barcodeError)
                {
                    messages.Add(barcodeError);
                }
                else if (!seenBarcodesInFile.Add(externalBarcode))
                {
                    messages.Add($"Barcode '{externalBarcode}' staat meermaals in het bestand.");
                }
                else if (action == ImportRowAction.Create && knownBarcodes.Contains(externalBarcode))
                {
                    messages.Add($"Barcode '{externalBarcode}' is al in gebruik.");
                }
                else if (action == ImportRowAction.Update)
                {
                    // Never silently re-barcode an existing package via import: ambiguous intent.
                    messages.Add("Externe barcodes kunnen niet via import worden gewijzigd; gebruik heretiketteren.");
                }
            }

            if (externalReference is not null && action == ImportRowAction.Create
                && !seenReferencesInFile.Add(externalReference))
            {
                messages.Add($"Externe referentie '{externalReference}' staat meermaals in het bestand.");
            }

            if (messages.Count > 0)
            {
                action = ImportRowAction.Error;
            }

            rows.Add(new ParsedRow(
                new ImportRowResult(rowNumber, action, packageNumber, description ?? "?", messages),
                packageNumber, description, quantity, unitType, externalBarcode, externalReference,
                customerReference, weight, volume, length, width, height,
                mandatory, fragile, temperature, signature, notes, raw));
        }

        if (rows.Count == 0)
        {
            return new ParseOutcome([], existingByNumber, "Het bestand bevat geen datarijen.");
        }

        return new ParseOutcome(rows, existingByNumber, null);
    }

    private string GenerateNumber(Modules.Tenancy.Entities.TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"PKG-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        }

        var number = $"{settings.PackageNumberPrefix}{settings.PackageNumberNextValue:00000}";
        settings.PackageNumberNextValue++;
        return number;
    }

    /// <summary>Error workbook: the offending rows verbatim + a Fouten column (text values only).</summary>
    private static string BuildErrorWorkbook(IReadOnlyList<ParsedRow> errorRows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Fouten");
        sheet.Cell(1, 1).SetValue("Rij").Style.Font.Bold = true;
        for (var column = 0; column < Headers.Length; column += 1)
        {
            sheet.Cell(1, column + 2).SetValue(Headers[column]).Style.Font.Bold = true;
        }

        sheet.Cell(1, Headers.Length + 2).SetValue("Fouten").Style.Font.Bold = true;

        var rowIndex = 2;
        foreach (var row in errorRows)
        {
            sheet.Cell(rowIndex, 1).SetValue(row.Result.RowNumber);
            for (var column = 0; column < row.RawCells.Count; column += 1)
            {
                sheet.Cell(rowIndex, column + 2).SetValue(row.RawCells[column]);
            }

            sheet.Cell(rowIndex, Headers.Length + 2).SetValue(string.Join("; ", row.Result.Messages));
            rowIndex += 1;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal? ParseDecimal(string value, string label, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value.Replace(',', '.'),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        messages.Add($"{label} '{value}' is geen geldig getal.");
        return null;
    }

    private static bool? ParseBool(string value, string label, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "ja" or "true" or "1" or "yes":
                return true;
            case "nee" or "false" or "0" or "no":
                return false;
            default:
                messages.Add($"{label} '{value}' is geen ja/nee-waarde.");
                return null;
        }
    }

    private static PackageUnitType? ParseUnit(string value, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TransportationService.Api.Common.EnumParsing.TryParseDefined<PackageUnitType>(value.Trim(), out var parsed))
        {
            return parsed;
        }

        messages.Add($"Eenheid '{value}' is onbekend.");
        return null;
    }
}
