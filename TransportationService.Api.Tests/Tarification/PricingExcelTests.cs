using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>
/// Rate-table Excel export and validated round-trip import: header contract, round-trip
/// identity via RegelId (never row position), classification (added/updated/removed),
/// row-level validation, the two commit modes and tenant isolation.
/// </summary>
public class PricingExcelTests
{
    private static readonly string[] ExpectedHeaders =
    [
        "RegelId", "Naam", "Basis", "Eenheid", "Zone", "Prioriteit",
        "Staffel van", "Staffel tot", "Gewicht tot (kg)", "Volume tot (m³)", "Laadmeter tot",
        "Staffelprijs", "Prijs per extra", "Eenheidsprijs", "Basisbedrag",
        "Minimum", "Maximum", "Min. aantal", "Afrondingsstap", "Staffelmodus",
        "Geldig van", "Geldig tot",
    ];

    private sealed record Harness(
        SqliteTestDbContext Db, PricingAdminService Admin, PricingExcelService Excel, Guid TenantId, Guid PalletUnitId, Guid ZoneId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.PricingZones.Add(new PricingZone { Id = zoneId, TenantId = tenantId, Code = "Z1", Name = "Zone 1", IsActive = true });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var excel = new PricingExcelService(db.Context, tenant, audit, admin);
        return new Harness(db, admin, excel, tenantId, palletUnitId, zoneId);
    }

    /// <summary>One QuantityBracket rule (3 brackets incl. weight caps) + one PerKm rule.</summary>
    private static async Task<(Guid AgreementId, Guid BracketRuleId, Guid PerKmRuleId)> SeedAgreementAsync(Harness h)
    {
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Distributie België", new DateOnly(2026, 1, 1), null, true, null, null, null), CancellationToken.None);

        var bracketRule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, h.ZoneId,
            "Europallet Z1", new DateOnly(2026, 1, 1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null, 100m),
                new SavePriceRuleBracketRequest(2, 3, 45m, null, 500m),
                new SavePriceRuleBracketRequest(4, null, 40m, 5m),
            ],
            agreement.Id), CancellationToken.None);

        var perKmRule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, null, PriceRuleBasis.PerKm, null,
            "Afstand", new DateOnly(2026, 1, 1), null, true, 1.25m, null, null,
            agreement.Id, BaseAmount: 10m), CancellationToken.None);

        return (agreement.Id, bracketRule.Id, perKmRule.Id);
    }

    // ------------------------------------------------------------- workbook helpers

    private static IXLWorksheet OpenSheet(byte[] bytes, out XLWorkbook workbook)
    {
        workbook = new XLWorkbook(new MemoryStream(bytes));
        return workbook.Worksheet("Tarieven");
    }

    private static byte[] Mutate(byte[] bytes, Action<IXLWorksheet> mutation)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Tarieven");
        mutation(sheet);
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static byte[] SetDecimal(byte[] bytes, int row, int column, decimal value) =>
        Mutate(bytes, sheet => sheet.Cell(row, column).SetValue(value));

    private static byte[] SetText(byte[] bytes, int row, int column, string value) =>
        Mutate(bytes, sheet => sheet.Cell(row, column).SetValue(value));

    private static byte[] Clear(byte[] bytes, int row, int column) =>
        Mutate(bytes, sheet => sheet.Cell(row, column).Clear());

    private static byte[] DeleteRow(byte[] bytes, int row) =>
        Mutate(bytes, sheet => sheet.Row(row).Delete());

    private static byte[] AppendRow(byte[] bytes, params (int Column, object Value)[] cells) =>
        Mutate(bytes, sheet =>
        {
            var row = (sheet.LastRowUsed()?.RowNumber() ?? 1) + 1;
            foreach (var (column, value) in cells)
            {
                switch (value)
                {
                    case decimal d: sheet.Cell(row, column).SetValue(d); break;
                    case string s: sheet.Cell(row, column).SetValue(s); break;
                    default: throw new NotSupportedException();
                }
            }
        });

    /// <summary>Locates the row whose RegelId column equals <paramref name="ruleId"/> (1-based).</summary>
    private static int RowOf(byte[] bytes, Guid ruleId)
    {
        var sheet = OpenSheet(bytes, out var workbook);
        using var _ = workbook;
        var lastRow = sheet.LastRowUsed()!.RowNumber();
        for (var row = 2; row <= lastRow; row += 1)
        {
            if (sheet.Cell(row, 1).GetString().Trim() == ruleId.ToString())
            {
                return row;
            }
        }

        throw new InvalidOperationException($"RegelId {ruleId} not found in workbook.");
    }

    private static MemoryStream ToStream(byte[] bytes) => new(bytes);

    // ------------------------------------------------------------- 1. Export

    [Fact]
    public async Task Export_ProducesExactHeaders_OneRowPerBracketPlusOrderMeasureRule()
    {
        var h = await SeedAsync();
        using var db = h.Db;
        var (agreementId, bracketRuleId, perKmRuleId) = await SeedAgreementAsync(h);

        var (file, fileName) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);

        Assert.NotNull(file);
        Assert.Contains("tarieventabel-", fileName);
        var sheet = OpenSheet(file!, out var workbook);
        using var disposeWorkbook = workbook;

        for (var column = 0; column < ExpectedHeaders.Length; column += 1)
        {
            Assert.Equal(ExpectedHeaders[column], sheet.Cell(1, column + 1).GetString());
        }

        var lastRow = sheet.LastRowUsed()!.RowNumber();
        Assert.Equal(5, lastRow); // header + 3 bracket rows + 1 PerKm row

        var regelIds = Enumerable.Range(2, 4).Select(r => sheet.Cell(r, 1).GetString()).ToList();
        Assert.All(regelIds, id => Assert.True(Guid.TryParse(id, out _)));
        Assert.Equal(3, regelIds.Count(id => id == bracketRuleId.ToString()));
        Assert.Equal(1, regelIds.Count(id => id == perKmRuleId.ToString()));

        var bracketRow = RowOf(file!, bracketRuleId);
        Assert.Equal("EUROPALLET", sheet.Cell(bracketRow, 4).GetString());
        Assert.Equal("Z1", sheet.Cell(bracketRow, 5).GetString());
        Assert.Equal("QuantityBracket", sheet.Cell(bracketRow, 3).GetString());

        var perKmRow = RowOf(file!, perKmRuleId);
        Assert.Equal("PerKm", sheet.Cell(perKmRow, 3).GetString());
        Assert.True(sheet.Cell(perKmRow, 7).IsEmpty()); // no staffel columns for a bracket-less rule
    }

    // ------------------------------------------------------------- 2. Round-trip

    [Fact]
    public async Task RoundTrip_UnchangedFile_ProducesNoChanges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, _) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);

        var (preview, error) = await h.Excel.PreviewAsync(agreementId, ToStream(file!), CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(preview);
        Assert.Equal(4, preview!.RowsFound);
        Assert.Equal(4, preview.RowsValid);
        Assert.Empty(preview.Errors);
        Assert.Empty(preview.Warnings);
        Assert.Empty(preview.Added);
        Assert.Empty(preview.Updated);
        Assert.Empty(preview.Removed);
    }

    // ------------------------------------------------------------- 3. Modify a price

    [Fact]
    public async Task ModifyUnitPrice_PreviewShowsSingleUpdate_CommitApplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, perKmRuleId) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        var perKmRow = RowOf(file!, perKmRuleId);
        var modified = SetDecimal(file!, perKmRow, 14, 1.40m); // Eenheidsprijs column

        var (preview, _) = await h.Excel.PreviewAsync(agreementId, ToStream(modified), CancellationToken.None);
        Assert.NotNull(preview);
        Assert.Empty(preview!.Added);
        Assert.Empty(preview.Removed);
        Assert.Single(preview.Updated);
        var change = preview.Updated[0];
        Assert.Equal(perKmRuleId, change.RuleId);
        Assert.Contains(change.FieldChanges!, f => f.Contains("Eenheidsprijs: 1,25 → 1,40"));

        var (commit, commitError) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(modified), CancellationToken.None);
        Assert.Null(commitError);
        Assert.NotNull(commit);
        Assert.Equal(0, commit!.Added);
        Assert.Equal(1, commit.Updated);

        var updatedRule = await h.Db.Context.PriceRules.SingleAsync(r => r.Id == perKmRuleId);
        Assert.Equal(1.40m, updatedRule.UnitPrice);
    }

    // ------------------------------------------------------------- 4. New rule without RegelId

    [Fact]
    public async Task NewRowWithoutRegelId_IsAdded_CommitCreatesRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, _) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        var withNewRow = AppendRow(file!,
            (2, "Statiegeld"), (3, "Fixed"), (14, 25m), (21, "2026-01-01"));

        var (preview, _) = await h.Excel.PreviewAsync(agreementId, ToStream(withNewRow), CancellationToken.None);
        Assert.NotNull(preview);
        Assert.Single(preview!.Added);
        Assert.Equal("Statiegeld", preview.Added[0].Name);
        Assert.Contains("Fixed", preview.Added[0].Summary!);
        Assert.Empty(preview.Updated);
        Assert.Empty(preview.Removed);

        var (commit, _) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(withNewRow), CancellationToken.None);
        Assert.NotNull(commit);
        Assert.Equal(1, commit!.Added);

        var created = await h.Db.Context.PriceRules.SingleAsync(r => r.AgreementId == agreementId && r.Name == "Statiegeld");
        Assert.Equal(PriceRuleBasis.Fixed, created.Basis);
        Assert.Equal(25m, created.UnitPrice);
    }

    // ------------------------------------------------------------- 5. Omitted rule -> Removed

    [Fact]
    public async Task OmittedRuleRows_AreListedRemoved_DeletedOnlyWhenApplyRemovalsSet()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, perKmRuleId) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        var perKmRow = RowOf(file!, perKmRuleId);
        var trimmed = DeleteRow(file!, perKmRow);

        var (preview, _) = await h.Excel.PreviewAsync(agreementId, ToStream(trimmed), CancellationToken.None);
        Assert.NotNull(preview);
        Assert.Single(preview!.Removed);
        Assert.Equal(perKmRuleId, preview.Removed[0].RuleId);

        var (keepResult, _) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(trimmed), CancellationToken.None);
        Assert.NotNull(keepResult);
        Assert.Equal(0, keepResult!.Removed);
        Assert.True(await h.Db.Context.PriceRules.AnyAsync(r => r.Id == perKmRuleId));

        var (deleteResult, _) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, true, null, null),
            ToStream(trimmed), CancellationToken.None);
        Assert.NotNull(deleteResult);
        Assert.Equal(1, deleteResult!.Removed);
        Assert.False(await h.Db.Context.PriceRules.AnyAsync(r => r.Id == perKmRuleId));
    }

    // ------------------------------------------------------------- 6. Row-level validation errors

    private static async Task<(Harness Harness, Guid AgreementId, byte[] File)> SeedWithExportAsync()
    {
        var h = await SeedAsync();
        var (agreementId, _, _) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        return (h, agreementId, file!);
    }

    private static async Task AssertBlocksWithRowErrorAsync(Harness h, Guid agreementId, byte[] badFile, int expectedRow, string messageFragment)
    {
        var (preview, _) = await h.Excel.PreviewAsync(agreementId, ToStream(badFile), CancellationToken.None);
        Assert.NotNull(preview);
        Assert.Contains(preview!.Errors, e => e.Row == expectedRow && e.Message.Contains(messageFragment));
        Assert.True(preview.RowsValid < preview.RowsFound);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(badFile), CancellationToken.None));
        Assert.Contains($"Rij {expectedRow}", ex.Message);
    }

    [Fact]
    public async Task UnknownBasis_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = SetText(file, row, 3, "Bogus");

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "Basis");
    }

    [Fact]
    public async Task UnknownUnitCode_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.QuantityBracket)).Id);
        var bad = SetText(file, row, 4, "NOPE");

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "Eenheid");
    }

    [Fact]
    public async Task UnknownZoneCode_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.QuantityBracket)).Id);
        var bad = SetText(file, row, 5, "ZZZ");

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "Zone");
    }

    [Fact]
    public async Task StaffelVanGreaterThanTot_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.QuantityBracket)).Id);
        var bad = SetDecimal(SetDecimal(file, row, 7, 9), row, 8, 1); // van=9, tot=1

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "Staffel van");
    }

    [Fact]
    public async Task NegativePrice_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = SetDecimal(file, row, 14, -5m);

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "negatief");
    }

    [Fact]
    public async Task InvalidDate_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = SetText(file, row, 21, "not-a-date");

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "datum");
    }

    [Fact]
    public async Task MissingPrice_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = Clear(file, row, 14); // remove Eenheidsprijs; PerKm row has no brackets either

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "Ontbrekende prijs");
    }

    [Fact]
    public async Task InvalidNumber_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = SetText(file, row, 14, "geen-getal");

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "getal");
    }

    // ------------------------------------------------------------- 6b. Malformed RegelId

    [Fact]
    public async Task MalformedRegelId_ProducesRowErrorAndBlocksCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = SetText(file, row, 1, "abc-123");

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "geen geldige GUID");
    }

    // ------------------------------------------------------------- 6c. Duplicate bracket row -> warning, not blocked

    [Fact]
    public async Task DuplicateBracketRow_ProducesWarning_DoesNotBlockCommit()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var bracketRule = await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.QuantityBracket);

        // Append an extra row for the SAME rule (same RegelId) repeating the exact same bracket
        // range (Staffel van/tot = 1/1) as an already-exported row for that rule.
        var withDuplicate = AppendRow(file,
            (1, bracketRule.Id.ToString()), (2, "Europallet Z1"), (3, "QuantityBracket"),
            (7, 1m), (8, 1m), (12, 50m));
        var sheetForRowLookup = OpenSheet(withDuplicate, out var lookupWorkbook);
        var duplicateRow = sheetForRowLookup.LastRowUsed()!.RowNumber();
        lookupWorkbook.Dispose();

        var (preview, error) = await h.Excel.PreviewAsync(agreementId, ToStream(withDuplicate), CancellationToken.None);
        Assert.Null(error);
        Assert.NotNull(preview);
        Assert.Contains(preview!.Warnings, w => w.Row == duplicateRow && w.Message.Contains("Dubbele rij"));
        Assert.Empty(preview.Errors);
        Assert.Equal(preview.RowsFound, preview.RowsValid); // a warning never reduces RowsValid

        var (commit, commitError) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(withDuplicate), CancellationToken.None);
        Assert.Null(commitError);
        Assert.NotNull(commit);
        Assert.Equal(0, commit!.Added);
        Assert.Equal(0, commit.Updated); // duplicate bracket ignored, so nothing actually changed

        var bracketCount = await h.Db.Context.PriceRuleBrackets.CountAsync(b => b.PriceRuleId == bracketRule.Id);
        Assert.Equal(3, bracketCount); // the duplicate was NOT added as a 4th bracket
    }

    // ------------------------------------------------------------- 6d. Empty Prioriteit cell -> warning (ledger cleanup)

    /// <summary>
    /// An empty Prioriteit cell silently defaults to 0, which can silently reorder precedence on
    /// re-import — warn when the source rule actually carried a non-zero priority; a rule whose
    /// priority was already 0 produces no warning (nothing actually changes).
    /// </summary>
    [Fact]
    public async Task EmptyPriorityCell_WarnsOnlyWhenSourceRuleHadNonZeroPriority_CommitStillAppliesZero()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, perKmRuleId) = await SeedAgreementAsync(h); // perKmRule has Priority == 0 (default)
        var priorityRule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, null, PriceRuleBasis.Fixed, null,
            "Vast bedrag met prioriteit", new DateOnly(2026, 1, 1), null, true, 20m, null, null,
            agreementId, Priority: 5), CancellationToken.None);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);

        var priorityRow = RowOf(file!, priorityRule.Id);
        var cleared = Clear(file!, priorityRow, 6); // Prioriteit column, for the non-zero-priority rule

        var (preview, error) = await h.Excel.PreviewAsync(agreementId, ToStream(cleared), CancellationToken.None);
        Assert.Null(error);
        Assert.NotNull(preview);
        Assert.Empty(preview!.Errors); // a warning never blocks
        Assert.Contains(preview.Warnings, w => w.Row == priorityRow
            && w.Message.Contains("Prioriteit leeg") && w.Message.Contains("0 gebruikt")
            && w.Message.Contains("Vast bedrag met prioriteit"));

        // Clearing the ALREADY-zero perKmRule's priority cell must not warn — nothing changed.
        var perKmRow = RowOf(cleared, perKmRuleId);
        var alsoCleared = Clear(cleared, perKmRow, 6);
        var (preview2, _) = await h.Excel.PreviewAsync(agreementId, ToStream(alsoCleared), CancellationToken.None);
        Assert.DoesNotContain(preview2!.Warnings, w => w.Row == perKmRow);

        var (commit, commitError) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(cleared), CancellationToken.None);
        Assert.Null(commitError);
        Assert.NotNull(commit);
        var updatedRule = await h.Db.Context.PriceRules.SingleAsync(r => r.Id == priorityRule.Id);
        Assert.Equal(0, updatedRule.Priority); // the warning informs, never blocks the (deliberate) 0
    }

    // ------------------------------------------------------------- 7. Foreign RegelId

    [Fact]
    public async Task RegelIdFromAnotherAgreement_ProducesRowError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, perKmRuleId) = await SeedAgreementAsync(h);
        var otherAgreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Andere tabel", new DateOnly(2026, 1, 1), null, true, null, null, null), CancellationToken.None);
        var otherRule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, null, PriceRuleBasis.Fixed, null, "Forfait", new DateOnly(2026, 1, 1), null, true, 10m, null, null,
            otherAgreement.Id), CancellationToken.None);

        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        var row = RowOf(file!, perKmRuleId);
        var bad = SetText(file!, row, 1, otherRule.Id.ToString());

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "andere tarieventabel");
    }

    [Fact]
    public async Task RegelIdThatDoesNotExist_ProducesRowError()
    {
        var (h, agreementId, file) = await SeedWithExportAsync();
        using var _ = h.Db;
        var row = RowOf(file, (await h.Db.Context.PriceRules.FirstAsync(r => r.Basis == PriceRuleBasis.PerKm)).Id);
        var bad = SetText(file, row, 1, Guid.NewGuid().ToString());

        await AssertBlocksWithRowErrorAsync(h, agreementId, bad, row, "bestaat niet");
    }

    // ------------------------------------------------------------- 8. DuplicateAsNewVersion

    [Fact]
    public async Task DuplicateAsNewVersion_LeavesSourceUntouched_AppliesFileToCopy()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, perKmRuleId) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        var perKmRow = RowOf(file!, perKmRuleId);
        var modified = SetDecimal(file!, perKmRow, 14, 1.40m);

        var (result, error) = await h.Excel.CommitAsync(
            agreementId,
            new PricingImportCommitRequest(PricingImportMode.DuplicateAsNewVersion, false, "Distributie België 2027", new DateOnly(2027, 1, 1)),
            ToStream(modified), CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.NotEqual(agreementId, result!.AgreementId);
        Assert.Equal("Distributie België 2027", result.Agreement.Name);
        Assert.Equal(0, result.Agreement.CustomerCount);

        var sourceRule = await h.Db.Context.PriceRules.SingleAsync(r => r.Id == perKmRuleId);
        Assert.Equal(1.25m, sourceRule.UnitPrice); // source untouched

        var copyRules = await h.Db.Context.PriceRules.Where(r => r.AgreementId == result.AgreementId).ToListAsync();
        Assert.Equal(2, copyRules.Count);
        var copyPerKm = copyRules.Single(r => r.Basis == PriceRuleBasis.PerKm);
        Assert.Equal(1.40m, copyPerKm.UnitPrice); // copy carries the file's change
        Assert.NotEqual(perKmRuleId, copyPerKm.Id);
    }

    // ------------------------------------------------------------- 9. Derived agreement refused

    [Fact]
    public async Task DerivedAgreement_ImportRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (baseAgreementId, _, _) = await SeedAgreementAsync(h);
        var derived = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "NL (afgeleid)", new DateOnly(2026, 1, 1), null, true, null, null, null,
            BaseAgreementId: baseAgreementId), CancellationToken.None);

        var (baseFile, _) = await h.Excel.ExportAsync(baseAgreementId, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Excel.CommitAsync(
            derived.Id, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            new MemoryStream(baseFile!), CancellationToken.None));
        Assert.Contains("afgeleide tabel", ex.Message);
    }

    // ------------------------------------------------------------- 10. Preview writes nothing

    [Fact]
    public async Task Preview_NeverWritesToTheDatabase()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, perKmRuleId) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        var perKmRow = RowOf(file!, perKmRuleId);
        var mutated = DeleteRow(SetDecimal(file!, perKmRow, 14, 9.99m), perKmRow);
        var withNewRow = AppendRow(mutated, (2, "Extra"), (3, "Fixed"), (14, 5m), (21, "2026-01-01"));

        var ruleCountBefore = await h.Db.Context.PriceRules.CountAsync();
        var bracketCountBefore = await h.Db.Context.PriceRuleBrackets.CountAsync();

        var (preview, _) = await h.Excel.PreviewAsync(agreementId, ToStream(withNewRow), CancellationToken.None);
        Assert.NotNull(preview);
        Assert.NotEmpty(preview!.Added);

        Assert.Equal(ruleCountBefore, await h.Db.Context.PriceRules.CountAsync());
        Assert.Equal(bracketCountBefore, await h.Db.Context.PriceRuleBrackets.CountAsync());
    }

    // ------------------------------------------------------------- Tenant isolation

    [Fact]
    public async Task Preview_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreementId, _, _) = await SeedAgreementAsync(h);
        var (file, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignAudit = new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null));
        var foreignAdmin = new PricingAdminService(h.Db.Context, foreignTenant, foreignAudit);
        var foreignExcel = new PricingExcelService(h.Db.Context, foreignTenant, foreignAudit, foreignAdmin);

        var (preview, error) = await foreignExcel.PreviewAsync(agreementId, new MemoryStream(file!), CancellationToken.None);
        Assert.Null(preview);
        Assert.NotNull(error);

        var (exportFile, _) = await foreignExcel.ExportAsync(agreementId, CancellationToken.None);
        Assert.Null(exportFile);
    }
}
