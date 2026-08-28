using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
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
/// Sprint 4D/4F — a customer's own rate sheet imports through a reusable mapping profile, and
/// every import is traceable and recognisable as a re-import. All of it feeds the SAME pricing
/// rules; there is no Excel-specific pricing path.
/// </summary>
public class PricingImportMappingTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, PricingAdminService Admin, PricingExcelService Excel, Guid TenantId, Guid UnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.UnitTypes.Add(new UnitType { Id = unitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        return new Harness(db, admin, new PricingExcelService(db.Context, tenant, audit, admin), tenantId, unitId);
    }

    private static async Task<Guid> SeedAgreementAsync(Harness h)
    {
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Atlas Copco 2026", new DateOnly(2026, 1, 1), null, true, null, null, null), CancellationToken.None);
        return agreement!.Id;
    }

    /// <summary>A workbook in the CUSTOMER's own layout: other headers, a title row above them.</summary>
    private static byte[] CustomerWorkbook(string sheetName = "Tarieven 2026")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        sheet.Cell(1, 1).SetValue("Tarieflijst Atlas Copco — versie 2026");
        sheet.Cell(2, 1).SetValue("Omschrijving");
        sheet.Cell(2, 2).SetValue("Berekening");
        sheet.Cell(2, 3).SetValue("Tarief");
        sheet.Cell(2, 4).SetValue("Start");

        sheet.Cell(3, 1).SetValue("Distributie Antwerpen");
        sheet.Cell(3, 2).SetValue("PerKm");
        sheet.Cell(3, 3).SetValue(2.50);
        sheet.Cell(3, 4).SetValue("2026-01-01");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static MemoryStream ToStream(byte[] bytes) => new(bytes);

    private static async Task<Guid> AddProfileAsync(Harness h, string name = "Atlas Copco 2026", string sheet = "Tarieven 2026")
    {
        var profile = new PricingImportProfile
        {
            Id = Guid.NewGuid(),
            TenantId = h.TenantId,
            Name = name,
            HeaderRow = 2,
            SheetName = sheet,
            MappingJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["naam"] = "Omschrijving",
                ["basis"] = "Berekening",
                ["eenheidsprijs"] = "Tarief",
                ["geldigVan"] = "Start",
            }),
            IsActive = true,
        };
        h.Db.Context.PricingImportProfiles.Add(profile);
        await h.Db.Context.SaveChangesAsync();
        return profile.Id;
    }

    // ---------------------------------------------------------- custom mapping

    [Fact]
    public async Task WithoutAProfile_ACustomerLayoutIsRejectedWithAClearMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);

        var (preview, error) = await h.Excel.PreviewAsync(
            agreementId, ToStream(CustomerWorkbook()), null, "atlas.xlsx", CancellationToken.None);

        Assert.Null(preview);
        Assert.NotNull(error);
        // The user is told WHICH column is missing and what to do about it.
        Assert.Contains("Naam", error);
        Assert.Contains("mappingprofiel", error);
    }

    [Fact]
    public async Task WithAProfile_TheCustomersOwnLayoutImportsIntoTheNormalPriceRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);
        var profileId = await AddProfileAsync(h);

        var (preview, error) = await h.Excel.PreviewAsync(
            agreementId, ToStream(CustomerWorkbook()), profileId, "atlas.xlsx", CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(1, preview!.RowsFound);
        Assert.Equal(1, preview.RowsValid);
        Assert.Empty(preview.Errors);
        Assert.Single(preview.Added);

        var (result, commitError) = await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(CustomerWorkbook()), profileId, "atlas.xlsx", CancellationToken.None);

        Assert.Null(commitError);
        Assert.Equal(1, result!.Added);

        // It became an ordinary price rule of the existing engine.
        var rule = await h.Db.Context.PriceRules.AsNoTracking().SingleAsync(r => r.AgreementId == agreementId);
        Assert.Equal("Distributie Antwerpen", rule.Name);
        Assert.Equal(PriceRuleBasis.PerKm, rule.Basis);
        Assert.Equal(2.50m, rule.UnitPrice);
    }

    [Fact]
    public async Task StandardHeaders_AreMatchedByNameSoColumnOrderDoesNotMatter()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tarieven");
        // Standard headers, deliberately in a different order than the template writes them.
        sheet.Cell(1, 1).SetValue("Basis");
        sheet.Cell(1, 2).SetValue("Naam");
        sheet.Cell(1, 3).SetValue("Geldig van");
        sheet.Cell(1, 4).SetValue("Eenheidsprijs");
        sheet.Cell(2, 1).SetValue("PerKm");
        sheet.Cell(2, 2).SetValue("Rit Gent");
        sheet.Cell(2, 3).SetValue("2026-01-01");
        sheet.Cell(2, 4).SetValue(1.75);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var (preview, error) = await h.Excel.PreviewAsync(
            agreementId, ToStream(stream.ToArray()), null, "std.xlsx", CancellationToken.None);

        Assert.Null(error);
        Assert.Single(preview!.Added);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public async Task AnUnknownUnitCode_IsReportedAsARowError_NotSilentlySkipped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tarieven");
        sheet.Cell(1, 1).SetValue("Naam");
        sheet.Cell(1, 2).SetValue("Basis");
        sheet.Cell(1, 3).SetValue("Eenheid");
        sheet.Cell(1, 4).SetValue("Geldig van");
        sheet.Cell(2, 1).SetValue("Rit");
        sheet.Cell(2, 2).SetValue("PerUnit");
        sheet.Cell(2, 3).SetValue("BESTAATNIET");
        sheet.Cell(2, 4).SetValue("2026-01-01");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var (preview, _) = await h.Excel.PreviewAsync(
            agreementId, ToStream(stream.ToArray()), null, "bad.xlsx", CancellationToken.None);

        Assert.Contains(preview!.Errors, e => e.Message.Contains("BESTAATNIET"));
        Assert.Equal(0, preview.RowsValid);
    }

    // -------------------------------------------------------- history + re-import

    [Fact]
    public async Task Commit_RecordsTheImportInTheHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);
        var profileId = await AddProfileAsync(h);

        await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(CustomerWorkbook()), profileId, "atlas-2026.xlsx", CancellationToken.None);

        var run = await h.Db.Context.PricingImportRuns.AsNoTracking().SingleAsync();
        Assert.Equal(agreementId, run.AgreementId);
        Assert.Equal("atlas-2026.xlsx", run.FileName);
        Assert.Equal("Atlas Copco 2026", run.ProfileName);
        Assert.Equal(64, run.Checksum.Length); // SHA-256, hex
        Assert.Equal(1, run.RowsRead);
        Assert.Equal(1, run.Created);
        Assert.Equal(0, run.Failed);
        Assert.Equal(h.TenantId, run.TenantId);
    }

    [Fact]
    public async Task ReimportingTheExactSameFile_IsFlaggedOnThePreview()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);
        var profileId = await AddProfileAsync(h);
        var file = CustomerWorkbook();

        var (first, _) = await h.Excel.PreviewAsync(agreementId, ToStream(file), profileId, "atlas.xlsx", CancellationToken.None);
        Assert.False(first!.AlreadyImported);

        await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(file), profileId, "atlas.xlsx", CancellationToken.None);

        var (second, _) = await h.Excel.PreviewAsync(agreementId, ToStream(file), profileId, "atlas.xlsx", CancellationToken.None);
        Assert.True(second!.AlreadyImported);
        Assert.Equal("atlas.xlsx", second.PreviousImportFileName);
        Assert.NotNull(second.PreviousImportAt);
    }

    [Fact]
    public async Task ADifferentFile_IsNotFlaggedAsAReimport()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);
        var profileId = await AddProfileAsync(h);

        await h.Excel.CommitAsync(
            agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
            ToStream(CustomerWorkbook()), profileId, "atlas.xlsx", CancellationToken.None);

        // Same layout, different sheet name => different bytes => different checksum.
        var (preview, _) = await h.Excel.PreviewAsync(
            agreementId, ToStream(CustomerWorkbook("Tarieven 2027")), profileId, "atlas-2027.xlsx", CancellationToken.None);

        Assert.False(preview!.AlreadyImported);
    }

    [Fact]
    public async Task AFailedImport_LeavesNoHistoryClaimingItHappened()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);

        // No profile: the file cannot be read at all, so the commit throws before any write.
        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() =>
            h.Excel.CommitAsync(
                agreementId, new PricingImportCommitRequest(PricingImportMode.UpdateAgreement, false, null, null),
                ToStream(CustomerWorkbook()), null, "atlas.xlsx", CancellationToken.None));

        Assert.Empty(await h.Db.Context.PricingImportRuns.AsNoTracking().ToListAsync());
        Assert.Empty(await h.Db.Context.PriceRules.AsNoTracking().Where(r => r.AgreementId == agreementId).ToListAsync());
    }

    [Fact]
    public async Task AProfileFromAnotherTenant_IsIgnored()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);

        var foreign = new PricingImportProfile
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Vreemd", HeaderRow = 2,
            SheetName = "Tarieven 2026", MappingJson = "{\"naam\":\"Omschrijving\"}", IsActive = true,
        };
        h.Db.Context.PricingImportProfiles.Add(foreign);
        await h.Db.Context.SaveChangesAsync();

        // The profile is not visible to this tenant, so the standard headers apply and the
        // customer layout is rejected — never silently read with someone else's mapping.
        var (preview, error) = await h.Excel.PreviewAsync(
            agreementId, ToStream(CustomerWorkbook()), foreign.Id, "atlas.xlsx", CancellationToken.None);

        Assert.Null(preview);
        Assert.NotNull(error);
    }

    // ---------------------------------------------------------------- overlaps

    private static byte[] StandardWorkbook(params (string Name, string Basis, decimal From, decimal? To, decimal Price, string From2, string? Until)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tarieven");
        var headers = new[] { "Naam", "Basis", "Staffel van", "Staffel tot", "Staffelprijs", "Geldig van", "Geldig tot" };
        for (var i = 0; i < headers.Length; i += 1) sheet.Cell(1, i + 1).SetValue(headers[i]);
        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).SetValue(row.Name);
            sheet.Cell(r, 2).SetValue(row.Basis);
            sheet.Cell(r, 3).SetValue(row.From);
            if (row.To is { } to) sheet.Cell(r, 4).SetValue(to);
            sheet.Cell(r, 5).SetValue(row.Price);
            sheet.Cell(r, 6).SetValue(row.From2);
            if (row.Until is { } until) sheet.Cell(r, 7).SetValue(until);
            r += 1;
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task OverlappingBrackets_AreAnError_NotSilentlyAccepted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);
        var file = StandardWorkbook(
            ("Pallets", "QuantityBracket", 1m, 10m, 50m, "2026-01-01", null),
            ("Pallets", "QuantityBracket", 5m, 20m, 40m, "2026-01-01", null));

        var (preview, _) = await h.Excel.PreviewAsync(agreementId, ToStream(file), null, "x.xlsx", CancellationToken.None);

        Assert.Contains(preview!.Errors, e => e.Message.Contains("overlappen"));
        Assert.Equal(0, preview.RowsValid);
    }

    [Fact]
    public async Task SameRuleWithOverlappingValidity_IsReportedAsAConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementId = await SeedAgreementAsync(h);
        // Distinct groups (RegelId-less rows group by Name+Basis, so use differing basis to keep
        // them separate) would not conflict; two identical name+basis rows land in ONE group.
        // The conflict case is two EXISTING rules re-imported with overlapping windows — build
        // them through the admin service first.
        var first = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.UnitId, PriceRuleBasis.PerUnit, null, "Rit", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), true, 10m, null, null, AgreementId: agreementId), CancellationToken.None);
        var second = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.UnitId, PriceRuleBasis.PerUnit, null, "Rit", new DateOnly(2026, 7, 1), null, true, 12m, null, null, AgreementId: agreementId), CancellationToken.None);

        var (exported, _) = await h.Excel.ExportAsync(agreementId, CancellationToken.None);
        // Move the second rule's start INTO the first one's window.
        using var workbook = new XLWorkbook(new MemoryStream(exported!));
        var sheet = workbook.Worksheet("Tarieven");
        for (var r = 2; r <= 3; r += 1)
        {
            if (sheet.Cell(r, 1).GetString() == second!.Id.ToString()) sheet.Cell(r, 21).SetValue("2026-03-01");
        }
        using var edited = new MemoryStream();
        workbook.SaveAs(edited);

        var (preview, error) = await h.Excel.PreviewAsync(agreementId, ToStream(edited.ToArray()), null, "x.xlsx", CancellationToken.None);

        Assert.Null(error);
        Assert.Contains(preview!.Warnings, w => w.Message.Contains("Conflict"));
    }

    // ----------------------------------------------------------------- columns

    [Fact]
    public void MappingJson_ThatIsCorrupt_FallsBackToStandardHeaders()
    {
        var mapping = PricingImportColumns.ParseMapping("{ this is not json");
        Assert.Empty(mapping);
    }

    [Fact]
    public void EveryCanonicalField_HasAUniqueKeyAndStandardHeader()
    {
        var keys = PricingImportColumns.All.Select(c => c.Key).ToList();
        var headers = PricingImportColumns.All.Select(c => c.StandardHeader).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(headers.Count, headers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Only the rule name is indispensable; blocking on more would reject usable sheets.
        Assert.Equal(["naam"], PricingImportColumns.All.Where(c => c.Required).Select(c => c.Key).ToArray());
    }
}
