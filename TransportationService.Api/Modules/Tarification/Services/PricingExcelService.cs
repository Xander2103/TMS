using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Services;

public interface IPricingExcelService
{
    /// <summary>Null file = the agreement does not exist for this tenant.</summary>
    Task<(byte[]? File, string? FileName)> ExportAsync(Guid agreementId, CancellationToken cancellationToken);

    /// <summary>Null preview + non-null error = a file-level problem (not a workbook, empty, too big).</summary>
    Task<(PricingImportPreviewDto? Preview, string? Error)> PreviewAsync(
        Guid agreementId, Stream workbook, Guid? profileId, string? fileName, CancellationToken cancellationToken);

    /// <summary>
    /// The header texts of an uploaded workbook, for the mapping step of the wizard. Explicit
    /// headerRow/sheetName (what the operator is typing) win over the saved profile's values.
    /// </summary>
    Task<(IReadOnlyList<string>? Headers, string? Error)> ReadHeadersAsync(
        Stream workbook, Guid? profileId, int? headerRow, string? sheetName, CancellationToken cancellationToken);

    /// <summary>Null result + null error = the agreement does not exist. Row errors throw DomainValidationException.</summary>
    Task<(PricingImportCommitResultDto? Result, string? Error)> CommitAsync(
        Guid agreementId, PricingImportCommitRequest request, Stream workbook, Guid? profileId, string? fileName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Excel export/import for one tarieventabel's price rules, mirroring the customer import
/// architecture (template-free here since export doubles as the template): RegelId (the rule's
/// Guid) is the stable round-trip key, never row position. One row per bracket; bracket-less
/// rules get a single row with the staffel columns empty. Import never writes during preview;
/// commit re-validates the same way and applies everything in one transaction.
/// </summary>
public class PricingExcelService : IPricingExcelService
{
    private const int MaxRows = 2000;

    private static readonly string[] Headers =
    [
        "RegelId", "Naam", "Basis", "Eenheid", "Zone", "Prioriteit",
        "Staffel van", "Staffel tot", "Gewicht tot (kg)", "Volume tot (m³)", "Laadmeter tot",
        "Staffelprijs", "Prijs per extra", "Eenheidsprijs", "Basisbedrag",
        "Minimum", "Maximum", "Min. aantal", "Afrondingsstap", "Staffelmodus",
        "Geldig van", "Geldig tot",
    ];

    private static readonly CultureInfo NlCulture = CultureInfo.GetCultureInfo("nl-BE");

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IPricingAdminService _pricingAdmin;

    public PricingExcelService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        IPricingAdminService pricingAdmin)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _pricingAdmin = pricingAdmin;
    }

    private Guid TenantId => _tenantContext.TenantId;

    // ======================================================================
    // Export
    // ======================================================================

    public async Task<(byte[]? File, string? FileName)> ExportAsync(Guid agreementId, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return (null, null);
        }

        var rules = await _dbContext.PriceRules.AsNoTracking().Include(r => r.Brackets)
            .Where(r => r.TenantId == TenantId && r.AgreementId == agreementId)
            .OrderBy(r => r.Priority).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var (unitCodes, zoneCodes, _, _) = await LoadCodesAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Tarieven");
        for (var column = 0; column < Headers.Length; column += 1)
        {
            sheet.Cell(1, column + 1).SetValue(Headers[column]).Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var rule in rules)
        {
            var brackets = rule.Brackets.OrderBy(b => b.FromQuantity).ToList();
            if (brackets.Count == 0)
            {
                WriteRow(sheet, rowIndex, rule, null, unitCodes, zoneCodes);
                rowIndex += 1;
                continue;
            }

            foreach (var bracket in brackets)
            {
                WriteRow(sheet, rowIndex, rule, bracket, unitCodes, zoneCodes);
                rowIndex += 1;
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        var help = workbook.AddWorksheet("Uitleg");
        var basisValues = string.Join(", ", Enum.GetNames<PriceRuleBasis>());
        var unitList = unitCodes.Values.Count == 0 ? "(geen)" : string.Join(", ", unitCodes.Values.OrderBy(c => c));
        var zoneList = zoneCodes.Values.Count == 0 ? "(geen)" : string.Join(", ", zoneCodes.Values.OrderBy(c => c));
        var lines = new[]
        {
            "RegelId niet wijzigen — dit is de koppeling met de bestaande regel.",
            "Nieuwe regel toevoegen: laat RegelId leeg. Naam + Basis identificeren de nieuwe regel binnen dit bestand.",
            "Regel verwijderen: verwijder alle rijen van die regel uit dit bestand en vink bij het importeren 'Verwijderingen toepassen' aan.",
            "Eén rij per staffel; regels zonder staffel hebben één rij met de staffelkolommen leeg.",
            $"Toegestane waarden voor Basis: {basisValues}.",
            $"Beschikbare eenheidscodes: {unitList}.",
            $"Beschikbare zonecodes: {zoneList}.",
            "Datums in de vorm jjjj-MM-dd (bijvoorbeeld 2026-01-01).",
            "Getallen mogen met punt of komma als decimaalteken.",
            "Staffelmodus leeg = Absolute; vul 'PerNextUnit' in voor prijs per volgende eenheid.",
            $"Maximaal {MaxRows} rijen per import.",
        };
        for (var index = 0; index < lines.Length; index += 1)
        {
            help.Cell(index + 1, 1).SetValue(lines[index]);
        }

        help.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"tarieventabel-{SanitizeFileName(agreement.Name)}.xlsx";
        return (stream.ToArray(), fileName);
    }

    private static void WriteRow(
        IXLWorksheet sheet, int row, PriceRule rule, PriceRuleBracket? bracket,
        IReadOnlyDictionary<Guid, string> unitCodes, IReadOnlyDictionary<Guid, string> zoneCodes)
    {
        sheet.Cell(row, 1).SetValue(rule.Id.ToString());
        sheet.Cell(row, 2).SetValue(rule.Name);
        sheet.Cell(row, 3).SetValue(rule.Basis.ToString());
        sheet.Cell(row, 4).SetValue(rule.UnitTypeId is { } unitId ? unitCodes.GetValueOrDefault(unitId, "") : "");
        sheet.Cell(row, 5).SetValue(rule.ZoneId is { } zoneId ? zoneCodes.GetValueOrDefault(zoneId, "") : "");
        sheet.Cell(row, 6).SetValue(rule.Priority);

        if (bracket is not null)
        {
            sheet.Cell(row, 7).SetValue(bracket.FromQuantity);
            if (bracket.ToQuantity is { } to) sheet.Cell(row, 8).SetValue(to);
            if (bracket.WeightToKg is { } weight) sheet.Cell(row, 9).SetValue(weight);
            if (bracket.VolumeToM3 is { } volume) sheet.Cell(row, 10).SetValue(volume);
            if (bracket.LoadingMetersTo is { } loadingMeters) sheet.Cell(row, 11).SetValue(loadingMeters);
            sheet.Cell(row, 12).SetValue(bracket.Price);
            if (bracket.PricePerExtraUnit is { } extra) sheet.Cell(row, 13).SetValue(extra);
        }

        if (rule.UnitPrice is { } unitPrice) sheet.Cell(row, 14).SetValue(unitPrice);
        if (rule.BaseAmount is { } baseAmount) sheet.Cell(row, 15).SetValue(baseAmount);
        if (rule.MinimumAmount is { } minimum) sheet.Cell(row, 16).SetValue(minimum);
        if (rule.MaximumAmount is { } maximum) sheet.Cell(row, 17).SetValue(maximum);
        if (rule.MinimumQuantity is { } minQuantity) sheet.Cell(row, 18).SetValue(minQuantity);
        if (rule.QuantityRoundingStep is { } roundingStep) sheet.Cell(row, 19).SetValue(roundingStep);
        if (rule.BracketMode == BracketSelectionMode.PerNextUnit) sheet.Cell(row, 20).SetValue("PerNextUnit");
        sheet.Cell(row, 21).SetValue(rule.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (rule.EffectiveUntil is { } until) sheet.Cell(row, 22).SetValue(until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private async Task<(Dictionary<Guid, string> UnitCodes, Dictionary<Guid, string> ZoneCodes,
            Dictionary<string, Guid> UnitIdsByCode, Dictionary<string, Guid> ZoneIdsByCode)>
        LoadCodesAsync(CancellationToken cancellationToken)
    {
        var unitCodes = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == TenantId)
            .ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        var zoneCodes = await _dbContext.PricingZones.AsNoTracking()
            .Where(z => z.TenantId == TenantId)
            .ToDictionaryAsync(z => z.Id, z => z.Code, cancellationToken);
        var unitIdsByCode = unitCodes.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
        var zoneIdsByCode = zoneCodes.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
        return (unitCodes, zoneCodes, unitIdsByCode, zoneIdsByCode);
    }

    /// <summary>Null id (or an unknown/inactive one) simply means "standard headers".</summary>
    private async Task<PricingImportProfile?> LoadProfileAsync(Guid? profileId, CancellationToken cancellationToken)
    {
        if (profileId is not { } id) return null;
        return await _dbContext.Set<PricingImportProfile>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == TenantId && p.Id == id && p.IsActive, cancellationToken);
    }

    /// <summary>SHA-256 over the uploaded bytes; the stream is rewound for the parser.</summary>
    private static async Task<string> ChecksumAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek) stream.Position = 0;
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        if (stream.CanSeek) stream.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<(IReadOnlyList<string>? Headers, string? Error)> ReadHeadersAsync(
        Stream stream, Guid? profileId, int? headerRow, string? sheetName, CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(profileId, cancellationToken);
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch
        {
            return (null, "Het bestand is geen geldig Excel-werkboek (.xlsx).");
        }

        using var _ = workbook;
        var effectiveSheetName = string.IsNullOrWhiteSpace(sheetName) ? profile?.SheetName : sheetName.Trim();
        var sheet = FindSheet(workbook, effectiveSheetName);
        if (sheet is null) return (null, "Het werkboek bevat geen werkblad.");

        var effectiveHeaderRow = headerRow is { } explicitRow && explicitRow > 0
            ? explicitRow
            : profile?.HeaderRow is { } configured && configured > 0 ? configured : 1;
        return (PricingImportColumns.ReadHeaders(sheet, effectiveHeaderRow), null);
    }

    private static IXLWorksheet? FindSheet(XLWorkbook workbook, string? sheetName) =>
        (string.IsNullOrWhiteSpace(sheetName)
            ? workbook.Worksheets.FirstOrDefault(w => w.Name == "Tarieven")
            : workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase)))
        ?? workbook.Worksheets.FirstOrDefault();

    /// <summary>
    /// Records what an import did (sprint 4F). A successful run is written inside the import
    /// transaction (a rollback must not leave history claiming it happened); a rejected or
    /// failed run is written AFTER the rollback, so the history also shows what did NOT land.
    /// </summary>
    private void RecordRun(
        Guid agreementId, Guid targetAgreementId, string? fileName, string checksum,
        PricingImportProfile? profile, PricingImportCommitRequest request, ParseOutcome? outcome,
        int added, int updated, int removed, string status, string? error)
    {
        var failedRows = outcome?.Errors.Select(e => e.Row).Distinct().Count() ?? 0;
        _dbContext.Set<PricingImportRun>().Add(new PricingImportRun
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            AgreementId = agreementId,
            TargetAgreementId = targetAgreementId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? "onbekend.xlsx" : fileName.Trim(),
            Checksum = checksum,
            ProfileId = profile?.Id,
            ProfileName = profile?.Name,
            RowsRead = outcome?.RowsFound ?? 0,
            RowsValid = (outcome?.RowsFound ?? 0) - failedRows,
            Created = added,
            Updated = updated,
            Removed = removed,
            Failed = failedRows,
            Mode = request.Mode.ToString(),
            Status = status,
            Error = error is null ? null : error.Length > 1000 ? error[..1000] : error,
        });
    }

    /// <summary>
    /// Writes a Rejected/Failed history row on its own, outside any import transaction. The
    /// change tracker is cleared first so nothing of the abandoned import is retried with it.
    /// </summary>
    private async Task RecordUnsuccessfulRunAsync(
        Guid agreementId, string? fileName, string checksum, PricingImportProfile? profile,
        PricingImportCommitRequest request, ParseOutcome? outcome, string status, string error,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        RecordRun(agreementId, agreementId, fileName, checksum, profile, request, outcome, 0, 0, 0, status, error);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat([' ', '/', '\\']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-");
        }

        var trimmed = cleaned.Trim('-').ToLowerInvariant();
        return trimmed.Length == 0 ? "tabel" : trimmed;
    }

    // ======================================================================
    // Preview
    // ======================================================================

    public async Task<(PricingImportPreviewDto? Preview, string? Error)> PreviewAsync(
        Guid agreementId, Stream workbook, Guid? profileId, string? fileName, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return (null, "De tarieventabel bestaat niet.");
        }

        var profile = await LoadProfileAsync(profileId, cancellationToken);
        var checksum = await ChecksumAsync(workbook, cancellationToken);
        var outcome = await ParseWorkbookAsync(agreementId, agreement.EffectiveFrom, workbook, profile, cancellationToken);
        if (outcome.FileError is not null)
        {
            return (null, outcome.FileError);
        }

        // Sprint 4F: importing the identical file again is usually a mistake, so it is surfaced
        // as a warning on the preview rather than silently repeated.
        var previousImport = await _dbContext.Set<PricingImportRun>().AsNoTracking()
            .Where(r => r.TenantId == TenantId && r.Checksum == checksum && r.AgreementId == agreementId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return (BuildPreviewDto(outcome, previousImport), null);
    }

    private static PricingImportPreviewDto BuildPreviewDto(ParseOutcome outcome, PricingImportRun? previousImport = null)
    {
        // Preview always evaluates against the SOURCE agreement (the id in the URL), so the
        // file's Geldig van/tot are the rule's own real dates here — no window-preservation needed.
        var changes = ClassifyChanges(outcome, preserveValidity: false);
        var rowsValid = outcome.RowsFound - outcome.Errors.Select(e => e.Row).Distinct().Count();
        return new PricingImportPreviewDto(
            outcome.RowsFound, rowsValid,
            outcome.Warnings.OrderBy(w => w.Row)
                .Select(w => new PricingImportRowMessageDto(w.Row, w.Message)).ToList(),
            outcome.Errors.OrderBy(e => e.Row)
                .Select(e => new PricingImportRowMessageDto(e.Row, e.Message)).ToList(),
            changes.AddedDto, changes.UpdatedDto, changes.RemovedDto,
            previousImport is not null,
            previousImport?.CreatedAt,
            previousImport?.FileName,
            outcome.MatchedByNameCount,
            outcome.PresentFields.OrderBy(f => f, StringComparer.Ordinal).ToList());
    }

    // ======================================================================
    // Commit
    // ======================================================================

    public async Task<(PricingImportCommitResultDto? Result, string? Error)> CommitAsync(
        Guid agreementId, PricingImportCommitRequest request, Stream workbook, Guid? profileId, string? fileName,
        CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return (null, null);
        }

        if (agreement.BaseAgreementId is not null)
        {
            throw new DomainValidationException(
                "Een afgeleide tabel heeft geen eigen regels; importeer in de basistabel.");
        }

        var profile = await LoadProfileAsync(profileId, cancellationToken);
        var checksum = await ChecksumAsync(workbook, cancellationToken);
        var outcome = await ParseWorkbookAsync(agreementId, agreement.EffectiveFrom, workbook, profile, cancellationToken);

        // Validation rejections are recorded in the history (D3) — as Rejected, never as an
        // import that happened — and then surfaced to the caller exactly as before.
        var rejection = outcome.FileError
            ?? (outcome.Errors.Count > 0
                ? "Import geblokkeerd door fouten: " + string.Join(" | ",
                    outcome.Errors.OrderBy(e => e.Row).Select(e => $"Rij {e.Row}: {e.Message}"))
                : null)
            ?? (request.Mode == PricingImportMode.DuplicateAsNewVersion
                && (string.IsNullOrWhiteSpace(request.NewName) || request.NewEffectiveFrom is null)
                ? "Kies een naam en een ingangsdatum voor de nieuwe versie."
                : null);
        if (rejection is not null)
        {
            await RecordUnsuccessfulRunAsync(agreementId, fileName, checksum, profile, request,
                outcome.FileError is null ? outcome : null, PricingImportRunStatus.Rejected, rejection, cancellationToken);
            throw new DomainValidationException(rejection);
        }

        try
        {
            return await ApplyCommitAsync(agreementId, agreement, request, profile, checksum, fileName, outcome, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The transaction is already disposed (rolled back) here; the Failed row is written
            // on its own so the history explains why nothing landed.
            await RecordUnsuccessfulRunAsync(agreementId, fileName, checksum, profile, request, outcome,
                PricingImportRunStatus.Failed,
                ex.InnerException?.Message is { Length: > 0 } inner ? $"{ex.Message} {inner}" : ex.Message,
                cancellationToken);
            throw;
        }
    }

    private async Task<(PricingImportCommitResultDto? Result, string? Error)> ApplyCommitAsync(
        Guid agreementId, PricingAgreement agreement, PricingImportCommitRequest request,
        PricingImportProfile? profile, string checksum, string? fileName, ParseOutcome outcome,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        PricingAgreement targetAgreement;
        var effectiveOutcome = outcome;

        if (request.Mode == PricingImportMode.DuplicateAsNewVersion)
        {
            var prepared = await _pricingAdmin.PrepareAgreementDuplicateAsync(
                agreementId,
                new DuplicateAgreementRequest(request.NewName!.Trim(), request.NewEffectiveFrom!.Value, CloseSource: false),
                cancellationToken);
            if (prepared is null)
            {
                return (null, null);
            }

            var (newAgreement, ruleIdMap) = prepared.Value;
            // Persist the plain duplicate first — a normal EF query can then see the copy's rules
            // to apply the file's changes on top, all inside the SAME transaction.
            await _dbContext.SaveChangesAsync(cancellationToken);

            targetAgreement = newAgreement;
            var translatedGroups = outcome.CleanGroups
                .Select(g => g.RegelId is { } sourceId && ruleIdMap.TryGetValue(sourceId, out var copyId)
                    ? g with { RegelId = copyId }
                    : g)
                .ToList();
            var copyExistingRules = await _dbContext.PriceRules.Include(r => r.Brackets)
                .Where(r => r.TenantId == TenantId && r.AgreementId == newAgreement.Id)
                .ToListAsync(cancellationToken);
            effectiveOutcome = outcome with { CleanGroups = translatedGroups, ExistingRules = copyExistingRules };
        }
        else
        {
            targetAgreement = agreement;
        }

        // The exported file freezes the SOURCE's validity window. For a brand-new version, the
        // copy's rules already carry the NEW window (PrepareAgreementDuplicateAsync); re-applying
        // the file's (stale) Geldig van/tot to a matched rule would silently revert the new
        // version back to the old dates, so matched rules keep their own window in this mode.
        var preserveValidity = request.Mode == PricingImportMode.DuplicateAsNewVersion;
        var changes = ClassifyChanges(effectiveOutcome, preserveValidity);

        foreach (var group in changes.ToAdd)
        {
            _dbContext.PriceRules.Add(BuildNewRule(group, targetAgreement, effectiveOutcome.UnitIdsByCode, effectiveOutcome.ZoneIdsByCode));
        }

        foreach (var (group, existing) in changes.ToUpdate)
        {
            ApplyUpdate(existing, group, effectiveOutcome.UnitIdsByCode, effectiveOutcome.ZoneIdsByCode, preserveValidity,
                effectiveOutcome.PresentFields);
        }

        var removedCount = 0;
        if (request.ApplyRemovals)
        {
            foreach (var rule in changes.ToRemove)
            {
                _dbContext.PriceRules.Remove(rule);
                removedCount += 1;
            }
        }

        // Written before the save so the history row shares the import's transaction: a
        // rollback must not leave history claiming the import happened.
        RecordRun(agreementId, targetAgreement.Id, fileName, checksum, profile, request, outcome,
            changes.ToAdd.Count, changes.ToUpdate.Count, removedCount, PricingImportRunStatus.Succeeded, null);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", targetAgreement.Id.ToString(), "imported", null,
            new
            {
                request.Mode, SourceAgreementId = agreementId, FileName = fileName, Checksum = checksum,
                ProfileId = profile?.Id,
                Added = changes.ToAdd.Count, Updated = changes.ToUpdate.Count, Removed = removedCount,
            }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var agreementDto = await _pricingAdmin.GetAgreementAsync(targetAgreement.Id, cancellationToken);
        var result = new PricingImportCommitResultDto(
            targetAgreement.Id, agreementDto!, changes.ToAdd.Count, changes.ToUpdate.Count, removedCount);
        return (result, null);
    }

    private PriceRule BuildNewRule(
        RuleGroup group, PricingAgreement agreement,
        IReadOnlyDictionary<string, Guid> unitIdsByCode, IReadOnlyDictionary<string, Guid> zoneIdsByCode)
    {
        var rule = new PriceRule
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            AgreementId = agreement.Id,
            CustomerId = agreement.CustomerId,
            Name = group.Name.Trim(),
            Currency = agreement.Currency,
            Basis = group.Basis,
            UnitTypeId = group.UnitCode is { } unitCode ? unitIdsByCode[unitCode] : null,
            ZoneId = group.ZoneCode is { } zoneCode ? zoneIdsByCode[zoneCode] : null,
            Priority = group.Priority,
            UnitPrice = group.UnitPrice,
            BaseAmount = group.BaseAmount,
            MinimumAmount = group.MinimumAmount,
            MaximumAmount = group.MaximumAmount,
            MinimumQuantity = group.MinimumQuantity,
            QuantityRoundingStep = group.QuantityRoundingStep,
            BracketMode = group.BracketMode,
            EffectiveFrom = group.EffectiveFrom ?? agreement.EffectiveFrom,
            EffectiveUntil = group.EffectiveUntil,
            IsActive = true,
        };
        foreach (var bracket in group.Brackets)
        {
            rule.Brackets.Add(CloneBracket(bracket, rule.Id));
        }

        return rule;
    }

    /// <summary>
    /// D1: only the fields the file actually supplies are written. A partial-column file (a
    /// customer sheet with just name/basis/price) must not wipe min/max/zone/validity/brackets
    /// on every matched rule.
    /// </summary>
    private void ApplyUpdate(
        PriceRule existing, RuleGroup group,
        IReadOnlyDictionary<string, Guid> unitIdsByCode, IReadOnlyDictionary<string, Guid> zoneIdsByCode,
        bool preserveValidity, IReadOnlySet<string> present)
    {
        existing.Name = group.Name.Trim();
        existing.Basis = group.Basis;
        if (present.Contains("eenheid")) existing.UnitTypeId = group.UnitCode is { } unitCode ? unitIdsByCode[unitCode] : null;
        if (present.Contains("zone")) existing.ZoneId = group.ZoneCode is { } zoneCode ? zoneIdsByCode[zoneCode] : null;
        if (present.Contains("prioriteit")) existing.Priority = group.Priority;
        if (present.Contains("eenheidsprijs")) existing.UnitPrice = group.UnitPrice;
        if (present.Contains("basisbedrag")) existing.BaseAmount = group.BaseAmount;
        if (present.Contains("minimum")) existing.MinimumAmount = group.MinimumAmount;
        if (present.Contains("maximum")) existing.MaximumAmount = group.MaximumAmount;
        if (present.Contains("minAantal")) existing.MinimumQuantity = group.MinimumQuantity;
        if (present.Contains("afrondingsstap")) existing.QuantityRoundingStep = group.QuantityRoundingStep;
        if (present.Contains("staffelmodus")) existing.BracketMode = group.BracketMode;
        if (!preserveValidity)
        {
            if (present.Contains("geldigVan")) existing.EffectiveFrom = group.EffectiveFrom ?? existing.EffectiveFrom;
            if (present.Contains("geldigTot")) existing.EffectiveUntil = group.EffectiveUntil;
        }

        if (!present.Contains(BracketField))
        {
            return;
        }

        // Full-replace, same pattern as PricingAdminService.UpdateRuleAsync — no explicit Added
        // state needed; EF tracks the freshly instantiated brackets via the parent navigation.
        _dbContext.PriceRuleBrackets.RemoveRange(existing.Brackets);
        existing.Brackets.Clear();
        foreach (var bracket in group.Brackets)
        {
            existing.Brackets.Add(CloneBracket(bracket, existing.Id));
        }
    }

    private PriceRuleBracket CloneBracket(PriceRuleBracket source, Guid priceRuleId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        PriceRuleId = priceRuleId,
        FromQuantity = source.FromQuantity,
        ToQuantity = source.ToQuantity,
        Price = source.Price,
        PricePerExtraUnit = source.PricePerExtraUnit,
        WeightToKg = source.WeightToKg,
        VolumeToM3 = source.VolumeToM3,
        LoadingMetersTo = source.LoadingMetersTo,
    };

    // ======================================================================
    // Classification (Added / Updated / Removed) — shared by preview and commit
    // ======================================================================

    private sealed record ChangeSet(
        List<RuleGroup> ToAdd, List<(RuleGroup Group, PriceRule Existing)> ToUpdate, List<PriceRule> ToRemove,
        List<PricingImportRuleChangeDto> AddedDto, List<PricingImportRuleChangeDto> UpdatedDto,
        List<PricingImportRuleChangeDto> RemovedDto);

    private static ChangeSet ClassifyChanges(ParseOutcome outcome, bool preserveValidity)
    {
        var toAdd = new List<RuleGroup>();
        var toUpdate = new List<(RuleGroup, PriceRule)>();
        var addedDto = new List<PricingImportRuleChangeDto>();
        var updatedDto = new List<PricingImportRuleChangeDto>();
        var referencedIds = new HashSet<Guid>();
        var existingById = outcome.ExistingRules.ToDictionary(r => r.Id);

        foreach (var group in outcome.CleanGroups)
        {
            if (group.RegelId is { } id && existingById.TryGetValue(id, out var existing))
            {
                referencedIds.Add(id);
                var fieldChanges = DiffRule(existing, group, outcome.UnitCodes, outcome.ZoneCodes, preserveValidity, outcome.PresentFields);
                if (fieldChanges.Count > 0)
                {
                    toUpdate.Add((group, existing));
                    updatedDto.Add(new PricingImportRuleChangeDto(group.Name, RuleId: existing.Id, FieldChanges: fieldChanges));
                }
            }
            else
            {
                toAdd.Add(group);
                addedDto.Add(new PricingImportRuleChangeDto(group.Name, Summary: SummarizeNewRule(group)));
            }
        }

        var toRemove = outcome.ExistingRules.Where(r => !referencedIds.Contains(r.Id)).ToList();
        var removedDto = toRemove.Select(r => new PricingImportRuleChangeDto(r.Name, RuleId: r.Id)).ToList();

        return new ChangeSet(toAdd, toUpdate, toRemove, addedDto, updatedDto, removedDto);
    }

    private static string SummarizeNewRule(RuleGroup group)
    {
        var parts = new List<string> { $"Basis: {group.Basis}" };
        if (group.UnitPrice is { } unitPrice) parts.Add($"Eenheidsprijs: {FormatAmount(unitPrice)}");
        if (group.Brackets.Count > 0) parts.Add($"{group.Brackets.Count} staffel(s)");
        if (group.UnitCode is not null) parts.Add($"Eenheid: {group.UnitCode}");
        if (group.ZoneCode is not null) parts.Add($"Zone: {group.ZoneCode}");
        return string.Join("; ", parts);
    }

    /// <summary>Same present-field gating as <see cref="ApplyUpdate"/>, so the preview shows exactly what commit will do.</summary>
    private static List<string> DiffRule(
        PriceRule existing, RuleGroup group,
        IReadOnlyDictionary<Guid, string> unitCodes, IReadOnlyDictionary<Guid, string> zoneCodes, bool preserveValidity,
        IReadOnlySet<string> present)
    {
        var changes = new List<string>();
        void Diff(string field, string label, string oldValue, string newValue)
        {
            if (present.Contains(field) && !string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add($"{label}: {oldValue} → {newValue}");
            }
        }

        var existingUnitCode = existing.UnitTypeId is { } existingUnitId ? unitCodes.GetValueOrDefault(existingUnitId) : null;
        var existingZoneCode = existing.ZoneId is { } existingZoneId ? zoneCodes.GetValueOrDefault(existingZoneId) : null;

        Diff("naam", "Naam", existing.Name, group.Name);
        Diff("basis", "Basis", existing.Basis.ToString(), group.Basis.ToString());
        Diff("eenheid", "Eenheid", existingUnitCode ?? "(geen)", group.UnitCode ?? "(geen)");
        Diff("zone", "Zone", existingZoneCode ?? "(geen)", group.ZoneCode ?? "(geen)");
        Diff("prioriteit", "Prioriteit", existing.Priority.ToString(CultureInfo.InvariantCulture), group.Priority.ToString(CultureInfo.InvariantCulture));
        Diff("eenheidsprijs", "Eenheidsprijs", FormatAmount(existing.UnitPrice), FormatAmount(group.UnitPrice));
        Diff("basisbedrag", "Basisbedrag", FormatAmount(existing.BaseAmount), FormatAmount(group.BaseAmount));
        Diff("minimum", "Minimum", FormatAmount(existing.MinimumAmount), FormatAmount(group.MinimumAmount));
        Diff("maximum", "Maximum", FormatAmount(existing.MaximumAmount), FormatAmount(group.MaximumAmount));
        Diff("minAantal", "Min. aantal", FormatAmount(existing.MinimumQuantity), FormatAmount(group.MinimumQuantity));
        Diff("afrondingsstap", "Afrondingsstap", FormatAmount(existing.QuantityRoundingStep), FormatAmount(group.QuantityRoundingStep));
        Diff("staffelmodus", "Staffelmodus", existing.BracketMode.ToString(), group.BracketMode.ToString());
        if (!preserveValidity)
        {
            // Skipped for DuplicateAsNewVersion: the exported file freezes the SOURCE's window,
            // while the copy already carries its own (new version) window — not a real change.
            var resolvedEffectiveFrom = group.EffectiveFrom ?? existing.EffectiveFrom;
            Diff("geldigVan", "Geldig van", existing.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                resolvedEffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Diff("geldigTot", "Geldig tot", existing.EffectiveUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "onbeperkt",
                group.EffectiveUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "onbeperkt");
        }

        Diff(BracketField, "Staffels", BracketSignature(existing.Brackets), BracketSignature(group.Brackets));

        return changes;
    }

    private static string BracketSignature(IEnumerable<PriceRuleBracket> brackets) =>
        string.Join("|", brackets.OrderBy(b => b.FromQuantity).Select(b =>
            $"{b.FromQuantity.ToString(CultureInfo.InvariantCulture)}-{b.ToQuantity?.ToString(CultureInfo.InvariantCulture) ?? "oo"}:" +
            $"{b.Price.ToString(CultureInfo.InvariantCulture)}/{b.PricePerExtraUnit?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));

    private static string FormatAmount(decimal? value) => value is null ? "leeg" : value.Value.ToString("N2", NlCulture);

    // ======================================================================
    // Parsing
    // ======================================================================

    /// <summary>One raw row's cell values, already type-converted where parsing succeeded.</summary>
    private sealed record RawRow(
        int RowNumber, Guid? RegelId, string Name, PriceRuleBasis? Basis, string? UnitCode, string? ZoneCode,
        int Priority,
        /// <summary>The Prioriteit cell's raw parsed value BEFORE defaulting to 0 — null = the cell was empty.</summary>
        decimal? PriorityRaw,
        decimal? StaffelVan, decimal? StaffelTot, decimal? GewichtTot, decimal? VolumeTot,
        decimal? LaadmeterTot, decimal? Staffelprijs, decimal? PrijsPerExtra, decimal? Eenheidsprijs,
        decimal? Basisbedrag, decimal? Minimum, decimal? Maximum, decimal? MinAantal, decimal? Afrondingsstap,
        BracketSelectionMode Staffelmodus, DateOnly? GeldigVan, DateOnly? GeldigTot);

    /// <summary>One reassembled rule (RegelId group, or Naam+Basis group for a new rule) + its brackets.</summary>
    private sealed record RuleGroup(
        Guid? RegelId, int AnchorRow, string Name, PriceRuleBasis Basis, string? UnitCode, string? ZoneCode,
        int Priority, decimal? UnitPrice, decimal? BaseAmount, decimal? MinimumAmount, decimal? MaximumAmount,
        decimal? MinimumQuantity, decimal? QuantityRoundingStep, BracketSelectionMode BracketMode,
        DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, List<PriceRuleBracket> Brackets);

    /// <summary>Pseudo-field key: the bracket columns as a whole (present when "Staffel van" is mapped).</summary>
    private const string BracketField = "staffels";

    private sealed record ParseOutcome(
        string? FileError, int RowsFound,
        List<(int Row, string Message)> Errors, List<(int Row, string Message)> Warnings,
        List<RuleGroup> CleanGroups, List<PriceRule> ExistingRules,
        Dictionary<Guid, string> UnitCodes, Dictionary<Guid, string> ZoneCodes,
        Dictionary<string, Guid> UnitIdsByCode, Dictionary<string, Guid> ZoneIdsByCode,
        /// <summary>Canonical field keys the file supplies (plus <see cref="BracketField"/>); updates only touch these.</summary>
        HashSet<string> PresentFields,
        /// <summary>RegelId-less rows matched to an existing rule on Naam+Basis+Eenheid+Zone.</summary>
        int MatchedByNameCount)
    {
        public static ParseOutcome ForFileError(string message) =>
            new(message, 0, [], [], [], [], [], [], [], [], [], 0);
    }

    private async Task<ParseOutcome> ParseWorkbookAsync(
        Guid agreementId, DateOnly agreementEffectiveFrom, Stream stream, PricingImportProfile? profile,
        CancellationToken cancellationToken)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch
        {
            return ParseOutcome.ForFileError("Het bestand is geen geldig Excel-werkboek (.xlsx).");
        }

        using var _ = workbook;
        var sheetName = profile?.SheetName;
        var sheet = (string.IsNullOrWhiteSpace(sheetName)
                ? workbook.Worksheets.FirstOrDefault(w => w.Name == "Tarieven")
                : workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase)))
            ?? workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            return ParseOutcome.ForFileError("Het werkboek bevat geen werkblad.");
        }

        // Sprint 4D: columns are matched on their HEADER (optionally renamed by a mapping
        // profile) instead of on a fixed position, so a customer's own layout imports as-is.
        var headerRow = profile?.HeaderRow is { } configured && configured > 0 ? configured : 1;
        var mapping = PricingImportColumns.ParseMapping(profile?.MappingJson);
        var resolution = PricingImportColumns.Resolve(sheet, headerRow, mapping);
        if (resolution.MissingRequired.Count > 0)
        {
            return ParseOutcome.ForFileError(
                "Deze kolommen ontbreken in het bestand: " + string.Join(", ", resolution.MissingRequired)
                + ". Kies een mappingprofiel of gebruik het standaardsjabloon.");
        }

        var columns = resolution.ColumnByField;
        // D1: what the file/profile actually supplies. Anything else is left untouched on an
        // update instead of being read as "set to empty".
        var presentFields = columns.Keys.ToHashSet(StringComparer.Ordinal);
        if (columns.ContainsKey("staffelVan")) presentFields.Add(BracketField);
        var regelIdMapped = columns.ContainsKey("regelId");
        var pricePresent = presentFields.Contains("eenheidsprijs") || presentFields.Contains(BracketField);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        if (lastRow - headerRow > MaxRows)
        {
            return ParseOutcome.ForFileError($"Maximaal {MaxRows} rijen per import; dit bestand bevat er {lastRow - headerRow}.");
        }

        var existingRules = await _dbContext.PriceRules.Include(r => r.Brackets)
            .Where(r => r.TenantId == TenantId && r.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        var existingById = existingRules.ToDictionary(r => r.Id);
        var (unitCodes, zoneCodes, unitIdsByCode, zoneIdsByCode) = await LoadCodesAsync(cancellationToken);

        var errors = new List<(int Row, string Message)>();
        var warnings = new List<(int Row, string Message)>();
        var rawRows = new List<RawRow>();

        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber += 1)
        {
            var row = sheet.Row(rowNumber);
            if (row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString())))
            {
                continue;
            }

            // A column the file does not have reads as empty rather than shifting the layout.
            IXLCell? Cell(string field) => columns.TryGetValue(field, out var column) ? row.Cell(column) : null;
            string Text(string field) => Cell(field)?.GetString().Trim() ?? string.Empty;

            var regelIdText = Text("regelId");
            var name = Text("naam");
            var basisText = Text("basis");
            var unitCode = NullIfEmpty(Text("eenheid"))?.ToUpperInvariant();
            var zoneCode = NullIfEmpty(Text("zone"))?.ToUpperInvariant();
            var priorityValue = ParseDecimalCell(Cell("prioriteit"), "Prioriteit", rowNumber, errors);
            var staffelVan = ParseDecimalCell(Cell("staffelVan"), "Staffel van", rowNumber, errors);
            var staffelTot = ParseDecimalCell(Cell("staffelTot"), "Staffel tot", rowNumber, errors);
            var gewichtTot = ParseDecimalCell(Cell("gewichtTot"), "Gewicht tot (kg)", rowNumber, errors);
            var volumeTot = ParseDecimalCell(Cell("volumeTot"), "Volume tot (m³)", rowNumber, errors);
            var laadmeterTot = ParseDecimalCell(Cell("laadmeterTot"), "Laadmeter tot", rowNumber, errors);
            // Monetary columns round to 2 decimals (the app-wide convention for EUR amounts);
            // quantity/measure columns above keep their parsed precision.
            var staffelprijs = Round2(ParseDecimalCell(Cell("staffelprijs"), "Staffelprijs", rowNumber, errors));
            var prijsPerExtra = Round2(ParseDecimalCell(Cell("prijsPerExtra"), "Prijs per extra", rowNumber, errors));
            var eenheidsprijs = Round2(ParseDecimalCell(Cell("eenheidsprijs"), "Eenheidsprijs", rowNumber, errors));
            var basisbedrag = Round2(ParseDecimalCell(Cell("basisbedrag"), "Basisbedrag", rowNumber, errors));
            var minimum = Round2(ParseDecimalCell(Cell("minimum"), "Minimum", rowNumber, errors));
            var maximum = Round2(ParseDecimalCell(Cell("maximum"), "Maximum", rowNumber, errors));
            var minAantal = ParseDecimalCell(Cell("minAantal"), "Min. aantal", rowNumber, errors);
            var afrondingsstap = ParseDecimalCell(Cell("afrondingsstap"), "Afrondingsstap", rowNumber, errors);
            var staffelmodusText = Text("staffelmodus");
            var geldigVan = ParseDateCell(Cell("geldigVan"), "Geldig van", rowNumber, errors);
            var geldigTot = ParseDateCell(Cell("geldigTot"), "Geldig tot", rowNumber, errors);

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add((rowNumber, "Naam is verplicht."));
            }

            var basis = ParseBasisCell(basisText, rowNumber, errors);

            Guid? regelId = null;
            if (regelIdText.Length > 0)
            {
                if (Guid.TryParse(regelIdText, out var parsedId))
                {
                    regelId = parsedId;
                }
                else
                {
                    errors.Add((rowNumber, $"RegelId '{regelIdText}' is geen geldige GUID."));
                }
            }

            if (unitCode is not null && !unitIdsByCode.ContainsKey(unitCode))
            {
                errors.Add((rowNumber, $"Eenheid '{unitCode}' is onbekend."));
            }

            if (zoneCode is not null && !zoneIdsByCode.ContainsKey(zoneCode))
            {
                errors.Add((rowNumber, $"Zone '{zoneCode}' is onbekend."));
            }

            if (staffelVan is not null && staffelTot is not null && staffelVan > staffelTot)
            {
                errors.Add((rowNumber, "Staffel van is groter dan Staffel tot."));
            }

            if (staffelVan is not null && staffelprijs is null)
            {
                errors.Add((rowNumber, "Staffelprijs ontbreekt voor deze staffelrij."));
            }

            foreach (var (label, value) in new (string Label, decimal? Value)[]
                     {
                         ("Staffelprijs", staffelprijs), ("Prijs per extra", prijsPerExtra),
                         ("Eenheidsprijs", eenheidsprijs), ("Basisbedrag", basisbedrag),
                         ("Minimum", minimum), ("Maximum", maximum),
                     })
            {
                if (value is < 0)
                {
                    errors.Add((rowNumber, $"{label} mag niet negatief zijn."));
                }
            }

            var staffelmodus = ParseBracketMode(staffelmodusText, rowNumber, errors);

            // R3: the engine stores priority as an int; anything outside a sane window is a
            // typo (or a price in the wrong column), not a precedence choice.
            if (priorityValue is { } priorityCheck && (priorityCheck < -1000 || priorityCheck > 1000))
            {
                errors.Add((rowNumber, $"Prioriteit '{priorityCheck.ToString(CultureInfo.InvariantCulture)}' moet tussen -1000 en 1000 liggen."));
                priorityValue = null;
            }

            rawRows.Add(new RawRow(
                rowNumber, regelId, name, basis, unitCode, zoneCode,
                (int)Math.Round(priorityValue ?? 0, 0, MidpointRounding.AwayFromZero), priorityValue,
                staffelVan, staffelTot, gewichtTot, volumeTot, laadmeterTot,
                staffelprijs, prijsPerExtra, eenheidsprijs, basisbedrag,
                minimum, maximum, minAantal, afrondingsstap, staffelmodus, geldigVan, geldigTot));
        }

        var rowsFound = rawRows.Count;
        if (rowsFound == 0)
        {
            return ParseOutcome.ForFileError("Het bestand bevat geen datarijen.");
        }

        // RegelId ownership: must belong to THIS agreement (and, by construction of the tenant-
        // scoped query, this tenant) — never trust the row's own claim.
        var candidateIds = rawRows.Where(r => r.RegelId is not null).Select(r => r.RegelId!.Value).Distinct().ToList();
        if (candidateIds.Count > 0)
        {
            var owned = await _dbContext.PriceRules.AsNoTracking()
                .Where(r => r.TenantId == TenantId && candidateIds.Contains(r.Id))
                .Select(r => new { r.Id, r.AgreementId })
                .ToListAsync(cancellationToken);
            var ownedMap = owned.ToDictionary(x => x.Id, x => x.AgreementId);
            foreach (var row in rawRows.Where(r => r.RegelId is not null))
            {
                if (!ownedMap.TryGetValue(row.RegelId!.Value, out var ownerAgreementId))
                {
                    errors.Add((row.RowNumber, $"RegelId '{row.RegelId}' bestaat niet."));
                }
                else if (ownerAgreementId != agreementId)
                {
                    errors.Add((row.RowNumber, $"RegelId '{row.RegelId}' behoort tot een andere tarieventabel."));
                }
            }
        }

        var rowsWithErrors = errors.Select(e => e.Row).ToHashSet();
        var cleanRows = rawRows.Where(r => !rowsWithErrors.Contains(r.RowNumber)).ToList();

        var groups = new List<RuleGroup>();
        var groupIndexByKey = new Dictionary<string, int>();
        // Source rows that contributed a bracket per group, so bracket-level errors can be
        // attributed to every row involved instead of only the group's first row.
        var bracketRowsByGroup = new Dictionary<int, List<int>>();
        // D2: without a RegelId column the file cannot reference rules by id, so an existing
        // rule with exactly the same Naam + Basis + Eenheid + Zone is treated as THAT rule (an
        // update), not as a new equal-specificity duplicate the engine would refuse to price.
        var existingByNameKey = existingRules
            .GroupBy(r => NameKey(r.Name, r.Basis,
                r.UnitTypeId is { } uid ? unitCodes.GetValueOrDefault(uid) : null,
                r.ZoneId is { } zid ? zoneCodes.GetValueOrDefault(zid) : null))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var matchedByName = new HashSet<Guid>();

        foreach (var row in cleanRows)
        {
            var regelId = row.RegelId;
            if (regelId is null && !regelIdMapped
                && existingByNameKey.TryGetValue(NameKey(row.Name, row.Basis!.Value, row.UnitCode, row.ZoneCode), out var matched))
            {
                regelId = matched.Id;
                matchedByName.Add(matched.Id);
            }

            // D5: a new rule is identified by Naam + Basis + Eenheid + Zone; rows that differ
            // only in unit or zone are different rules, never silently merged into one.
            var key = regelId is { } id ? $"ID:{id}" : "NEW:" + NameKey(row.Name, row.Basis!.Value, row.UnitCode, row.ZoneCode);
            if (!groupIndexByKey.TryGetValue(key, out var index))
            {
                index = groups.Count;
                groupIndexByKey[key] = index;
                groups.Add(new RuleGroup(
                    regelId, row.RowNumber, row.Name, row.Basis!.Value, row.UnitCode, row.ZoneCode, row.Priority,
                    row.Eenheidsprijs, row.Basisbedrag, row.Minimum, row.Maximum,
                    row.MinAantal, row.Afrondingsstap, row.Staffelmodus, row.GeldigVan, row.GeldigTot, []));

                // An empty Prioriteit cell silently defaults to 0, which can silently reorder
                // precedence on re-import — warn only when the SOURCE rule actually had a non-zero
                // priority (an empty cell on a genuinely new/zero-priority rule is not surprising).
                if (regelId is { } existingRuleId && row.PriorityRaw is null && presentFields.Contains("prioriteit")
                    && existingById.TryGetValue(existingRuleId, out var existingForPriority)
                    && existingForPriority.Priority != 0)
                {
                    warnings.Add((row.RowNumber, $"Prioriteit leeg — 0 gebruikt voor '{row.Name}'."));
                }
            }
            else
            {
                // D5: the rule-level (scalar) cells of every row in a group must agree. An
                // empty cell inherits the anchor row's value; a conflicting value is an error.
                var anchor = groups[index];
                var conflicts = ScalarConflicts(anchor, row);
                if (conflicts.Count > 0)
                {
                    errors.Add((row.RowNumber,
                        $"Rij hoort bij regel '{anchor.Name}' (rij {anchor.AnchorRow}) maar wijkt af in {string.Join(", ", conflicts)}; per regel mogen deze waarden maar één keer voorkomen."));
                }
                else
                {
                    groups[index] = FillBlanks(anchor, row);
                }
            }

            if (row.StaffelVan is { } from)
            {
                var group = groups[index];
                var duplicate = group.Brackets.Any(b => b.FromQuantity == from && b.ToQuantity == row.StaffelTot);
                if (duplicate)
                {
                    warnings.Add((row.RowNumber, "Dubbele rij: dezelfde staffel komt al voor bij deze regel."));
                }
                else
                {
                    bracketRowsByGroup.TryAdd(index, []);
                    bracketRowsByGroup[index].Add(row.RowNumber);
                    group.Brackets.Add(new PriceRuleBracket
                    {
                        FromQuantity = from, ToQuantity = row.StaffelTot, Price = row.Staffelprijs ?? 0,
                        PricePerExtraUnit = row.PrijsPerExtra, WeightToKg = row.GewichtTot,
                        VolumeToM3 = row.VolumeTot, LoadingMetersTo = row.LaadmeterTot,
                    });
                }
            }
        }

        foreach (var group in groups)
        {
            // A file without any price column leaves the matched rule's price alone (D1); only
            // a NEW rule, or a file that does carry price columns, must actually supply one.
            var hasPrice = group.UnitPrice is not null || group.Brackets.Count > 0;
            var isExisting = group.RegelId is { } gid && existingById.ContainsKey(gid);
            if (!hasPrice && (pricePresent || !isExisting))
            {
                errors.Add((group.AnchorRow, "Ontbrekende prijs: vul Staffelprijs of Eenheidsprijs in."));
            }

            // Audit fix (4E): overlapping brackets would make the engine's bracket selection
            // ambiguous, so they are an error, not a silently accepted row.
            var ordered = group.Brackets.OrderBy(b => b.FromQuantity).ToList();
            for (var i = 1; i < ordered.Count; i += 1)
            {
                var previousTo = ordered[i - 1].ToQuantity;
                if (previousTo is null || ordered[i].FromQuantity < previousTo.Value)
                {
                    var message = $"Staffels van '{group.Name}' overlappen ({ordered[i - 1].FromQuantity}–{previousTo?.ToString() ?? "open"} en {ordered[i].FromQuantity}–{ordered[i].ToQuantity?.ToString() ?? "open"}).";
                    IEnumerable<int> involvedRows = bracketRowsByGroup.TryGetValue(groups.IndexOf(group), out var rows)
                        ? rows.Append(group.AnchorRow).Distinct()
                        : [group.AnchorRow];
                    foreach (var involvedRow in involvedRows)
                    {
                        errors.Add((involvedRow, message));
                    }
                    break;
                }
            }
        }

        // Audit fix (4E): equal-specificity conflicts — two rules with the same name, basis,
        // unit and zone whose validity windows overlap. The engine would pick by priority (or
        // arbitrarily at equal priority), so the operator is told instead of finding out later.
        // Two rows in the FILE conflicting is a warning (the operator may be re-sequencing);
        // a file row conflicting with an existing rule the file does not reference (D2) is an
        // error, because committing it would create the exact duplicate the engine refuses.
        var referencedIds = groups.Where(g => g.RegelId is not null).Select(g => g.RegelId!.Value).ToHashSet();
        var candidates = groups
            .Select(g => (g.Name, g.Basis, g.UnitCode, g.ZoneCode, g.Priority,
                From: g.EffectiveFrom ?? DateOnly.MinValue, To: g.EffectiveUntil ?? DateOnly.MaxValue,
                Row: (int?)g.AnchorRow, ExistingId: (Guid?)null))
            .Concat(existingRules.Where(r => !referencedIds.Contains(r.Id)).Select(r => (
                r.Name, r.Basis,
                UnitCode: r.UnitTypeId is { } uid ? unitCodes.GetValueOrDefault(uid)?.ToUpperInvariant() : null,
                ZoneCode: r.ZoneId is { } zid ? zoneCodes.GetValueOrDefault(zid)?.ToUpperInvariant() : null,
                r.Priority, From: r.EffectiveFrom, To: r.EffectiveUntil ?? DateOnly.MaxValue,
                Row: (int?)null, ExistingId: (Guid?)r.Id)))
            .ToList();
        foreach (var pair in candidates
            .GroupBy(c => NameKey(c.Name, c.Basis, c.UnitCode, c.ZoneCode), StringComparer.Ordinal)
            .Where(g => g.Count() > 1))
        {
            var list = pair.ToList();
            for (var i = 0; i < list.Count; i += 1)
            {
                for (var j = i + 1; j < list.Count; j += 1)
                {
                    var a = list[i];
                    var b = list[j];
                    if (a.Row is null && b.Row is null) continue; // two existing rules: not this import's doing
                    if (!(a.From <= b.To && b.From <= a.To && a.Priority == b.Priority)) continue;

                    if (a.Row is { } aRow && b.Row is { } bRow)
                    {
                        warnings.Add((bRow,
                            $"Conflict: '{b.Name}' komt met dezelfde basis, eenheid, zone en prioriteit ook voor op rij {aRow} met een overlappende geldigheid."));
                    }
                    else
                    {
                        var fileRow = a.Row ?? b.Row!.Value;
                        var existingName = a.Row is null ? a.Name : b.Name;
                        errors.Add((fileRow,
                            $"Conflict: er bestaat al een regel '{existingName}' met dezelfde basis, eenheid, zone en prioriteit en een overlappende geldigheid. Vul de RegelId van die regel in om ze bij te werken, of verwijder de oude regel eerst."));
                    }
                }
            }
        }

        var invalidAnchors = errors.Select(e => e.Row).ToHashSet();
        var cleanGroups = groups.Where(g => !invalidAnchors.Contains(g.AnchorRow)).ToList();

        return new ParseOutcome(null, rowsFound, errors, warnings, cleanGroups, existingRules, unitCodes, zoneCodes,
            unitIdsByCode, zoneIdsByCode, presentFields, matchedByName.Count);
    }

    /// <summary>Exact Naam + Basis + Eenheid + Zone identity (case-insensitive), shared by D2 matching and D5 grouping.</summary>
    private static string NameKey(string name, PriceRuleBasis basis, string? unitCode, string? zoneCode) =>
        $"{name.Trim().ToUpperInvariant()}|{basis}|{unitCode?.Trim().ToUpperInvariant() ?? ""}|{zoneCode?.Trim().ToUpperInvariant() ?? ""}";

    /// <summary>Rule-level cells of a continuation row that are filled in AND differ from the anchor row.</summary>
    private static List<string> ScalarConflicts(RuleGroup anchor, RawRow row)
    {
        var conflicts = new List<string>();
        void Check<T>(string label, T? anchorValue, T? rowValue) where T : struct
        {
            if (rowValue is { } value && anchorValue is { } expected && !EqualityComparer<T>.Default.Equals(value, expected))
            {
                conflicts.Add(label);
            }
        }

        Check("Prioriteit", (decimal?)anchor.Priority, row.PriorityRaw is { } p ? Math.Round(p, 0, MidpointRounding.AwayFromZero) : null);
        Check("Eenheidsprijs", anchor.UnitPrice, row.Eenheidsprijs);
        Check("Basisbedrag", anchor.BaseAmount, row.Basisbedrag);
        Check("Minimum", anchor.MinimumAmount, row.Minimum);
        Check("Maximum", anchor.MaximumAmount, row.Maximum);
        Check("Min. aantal", anchor.MinimumQuantity, row.MinAantal);
        Check("Afrondingsstap", anchor.QuantityRoundingStep, row.Afrondingsstap);
        Check("Geldig van", anchor.EffectiveFrom, row.GeldigVan);
        Check("Geldig tot", anchor.EffectiveUntil, row.GeldigTot);
        return conflicts;
    }

    /// <summary>An anchor row that left a rule-level cell blank adopts the value a later row of the same rule supplies.</summary>
    private static RuleGroup FillBlanks(RuleGroup anchor, RawRow row) => anchor with
    {
        UnitPrice = anchor.UnitPrice ?? row.Eenheidsprijs,
        BaseAmount = anchor.BaseAmount ?? row.Basisbedrag,
        MinimumAmount = anchor.MinimumAmount ?? row.Minimum,
        MaximumAmount = anchor.MaximumAmount ?? row.Maximum,
        MinimumQuantity = anchor.MinimumQuantity ?? row.MinAantal,
        QuantityRoundingStep = anchor.QuantityRoundingStep ?? row.Afrondingsstap,
        EffectiveFrom = anchor.EffectiveFrom ?? row.GeldigVan,
        EffectiveUntil = anchor.EffectiveUntil ?? row.GeldigTot,
    };

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal? Round2(decimal? value) => value is null ? null : decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Numeric cells written by our own export are stored as native doubles; reading them back
    /// through <see cref="IXLCell.GetDouble"/> and rounding to 4 decimals removes the double→decimal
    /// float noise (e.g. 45.8 round-tripping as 45.799999999999997) without truncating anything a
    /// human would actually type. Text cells are parsed WITHOUT thousands separators (R2): a
    /// Belgian "1,250" is one euro twenty-five, never twelve hundred and fifty — nl-BE (comma
    /// decimal) is tried first, then the invariant culture (point decimal).
    /// </summary>
    private static decimal? ParseDecimalCell(IXLCell? cell, string label, int rowNumber, List<(int Row, string Message)> errors)
    {
        if (cell is null || cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            return Math.Round((decimal)cell.GetDouble(), 4, MidpointRounding.AwayFromZero);
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (ParseDecimalText(text) is { } parsed)
        {
            return parsed;
        }

        errors.Add((rowNumber, $"{label} '{text}' is geen geldig getal."));
        return null;
    }

    private const NumberStyles TextNumberStyles =
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    /// <summary>"1,25" → 1.25 (nl-BE), "1.25" → 1.25 (invariant), "1250" → 1250; "1,250" → 1.25, never 1250.</summary>
    public static decimal? ParseDecimalText(string text)
    {
        if (decimal.TryParse(text, TextNumberStyles, NlCulture, out var nl))
        {
            return nl;
        }

        if (decimal.TryParse(text, TextNumberStyles, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        return null;
    }

    private static DateOnly? ParseDateCell(IXLCell? cell, string label, int rowNumber, List<(int Row, string Message)> errors)
    {
        if (cell is null || cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(cell.GetDateTime());
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (ParseDateText(text) is { } parsed)
        {
            return parsed;
        }

        errors.Add((rowNumber, $"{label} '{text}' is geen geldige datum (gebruik jjjj-MM-dd of dd/MM/jjjj)."));
        return null;
    }

    /// <summary>
    /// R2: only ISO (2026-02-01) and the Belgian day-first form (01/02/2026 = 1 februari) are
    /// accepted. The culture-generic parsers read "01/02/2026" as 2 January, which silently
    /// shifts a tariff's validity; that ambiguity is refused rather than guessed.
    /// </summary>
    private static readonly string[] AcceptedDateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"];

    public static DateOnly? ParseDateText(string text) =>
        DateOnly.TryParseExact(text, AcceptedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static readonly string AllowedBasisValues = string.Join(", ", Enum.GetNames<PriceRuleBasis>());

    private static PriceRuleBasis? ParseBasisCell(string text, int rowNumber, List<(int Row, string Message)> errors)
    {
        if (text.Length == 0)
        {
            errors.Add((rowNumber, $"Basis is verplicht. Toegestane waarden: {AllowedBasisValues}."));
            return null;
        }

        if (Enum.TryParse<PriceRuleBasis>(text, ignoreCase: true, out var basis) && Enum.IsDefined(basis))
        {
            return basis;
        }

        errors.Add((rowNumber, $"Basis '{text}' is onbekend. Toegestane waarden: {AllowedBasisValues}."));
        return null;
    }

    private static BracketSelectionMode ParseBracketMode(string text, int rowNumber, List<(int Row, string Message)> errors)
    {
        if (text.Length == 0)
        {
            return BracketSelectionMode.Absolute;
        }

        // TryParseDefined: a numeric string would otherwise pass as an undefined enum value.
        if (Common.EnumParsing.TryParseDefined<BracketSelectionMode>(text, out var mode))
        {
            return mode;
        }

        errors.Add((rowNumber, $"Staffelmodus '{text}' is onbekend (Absolute of PerNextUnit)."));
        return BracketSelectionMode.Absolute;
    }
}
