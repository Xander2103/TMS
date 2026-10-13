using ClosedXML.Excel;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.OrderImport.Services;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.OrderImport;

/// <summary>
/// Excel-import profile management (2026-09): persisted per-tenant mapping profiles created
/// from the UI, deterministic sample-file analysis (alias catalog + saved-profile matching)
/// and the optional customer binding. The MappingJson round-trips through the importer's own
/// parser, so editor and importer can never disagree.
/// </summary>
public class OrderImportProfileTests
{
    private static readonly DateTime Now = new(2026, 09, 02, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, OrderImportService Sut, Guid TenantId, Guid CustomerId, Guid OtherCustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Atlas Copco", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Haven BV", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now));
        return new Harness(db, new OrderImportService(db.Context, tenant, audit, orders), tenantId, customerId, otherCustomerId);
    }

    /// <summary>Atlas-Copco-shaped sample: shuffled columns with English headers.</summary>
    private static byte[] BuildAtlasWorkbook(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Orders");
        string[] headers = ["Reference", "Destination ZIP", "Delivery city", "PAL", "KG", "Goods", "Internal note"];
        for (var column = 0; column < headers.Length; column += 1)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (var index = 0; index < rows.Length; index += 1)
        {
            for (var column = 0; column < rows[index].Length; column += 1)
            {
                if (rows[index][column] is { } value)
                {
                    sheet.Cell(index + 2, column + 1).SetValue(value.ToString());
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static SaveOrderImportProfileRequest AtlasProfileRequest(Guid? customerId = null) => new(
        "Atlas Copco Orders", "Engelse kolomkoppen", customerId, IsActive: true,
        HeaderRows: 1,
        Mapping: new Dictionary<string, string>
        {
            ["customerReference"] = "A",
            ["unloadingPostalCode"] = "B",
            ["unloadingCity"] = "C",
            ["quantity"] = "4",
            ["weightKg"] = "E",
            ["goodsDescription"] = "F",
        },
        SourceHeaders: ["Reference", "Destination ZIP", "Delivery city", "PAL", "KG", "Goods", "Internal note"]);

    [Fact]
    public async Task CreateUpdateDelete_RoundTrip_WithCustomerBindingAndHeaders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateProfileAsync(AtlasProfileRequest(h.CustomerId), CancellationToken.None);
        Assert.Equal("Atlas Copco Orders", created.Name);
        Assert.Equal(h.CustomerId, created.CustomerId);
        Assert.Equal("Atlas Copco", created.CustomerName);
        Assert.Equal(6, created.MappedFieldCount);
        // Column refs normalize to letters regardless of input form ("4" → "D").
        Assert.Equal("D", created.Mapping!["quantity"]);
        Assert.Contains("Destination ZIP", created.SourceHeaders!);

        var updated = await h.Sut.UpdateProfileAsync(created.Id,
            AtlasProfileRequest(h.CustomerId) with { Name = "Atlas Copco Orders v2", IsActive = false }, CancellationToken.None);
        Assert.Equal("Atlas Copco Orders v2", updated!.Name);
        Assert.False(updated.IsActive);
        // Inactive profiles hide from the import picker but stay manageable.
        Assert.DoesNotContain(await h.Sut.ListProfilesAsync(CancellationToken.None), p => p.Id == created.Id);
        Assert.Contains(await h.Sut.ListProfilesAsync(CancellationToken.None, includeInactive: true), p => p.Id == created.Id);

        Assert.True(await h.Sut.DeleteProfileAsync(created.Id, CancellationToken.None));
        Assert.DoesNotContain(await h.Sut.ListProfilesAsync(CancellationToken.None, includeInactive: true), p => p.Id == created.Id);
    }

    [Fact]
    public async Task Validation_RefusesDuplicateName_UnknownField_AndMissingUnloadingColumn()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateProfileAsync(AtlasProfileRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateProfileAsync(AtlasProfileRequest() with { Name = "atlas copco ORDERS" }, CancellationToken.None));

        var unknownField = AtlasProfileRequest() with
        {
            Name = "Fout 1",
            Mapping = new Dictionary<string, string> { ["unloadingCity"] = "A", ["nietBestaand"] = "B" },
        };
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateProfileAsync(unknownField, CancellationToken.None));
        Assert.Contains("Onbekend doelveld", ex.Message);

        // The importer's own rule: an unloading city/location column is mandatory.
        var noUnloading = AtlasProfileRequest() with
        {
            Name = "Fout 2",
            Mapping = new Dictionary<string, string> { ["customerReference"] = "A" },
        };
        var ex2 = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateProfileAsync(noUnloading, CancellationToken.None));
        Assert.Contains("losplaats", ex2.Message);
    }

    [Fact]
    public async Task CustomProfile_ImportsAShuffledFile_ThroughTheExistingImporter()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profile = await h.Sut.CreateProfileAsync(AtlasProfileRequest(h.CustomerId), CancellationToken.None);
        var file = BuildAtlasWorkbook(["AC-1001", "9000", "Gent", "4", "1200", "Compressoren", "negeer mij"]);

        var detail = await h.Sut.ImportAsync(profile.Id, h.CustomerId, "atlas.xlsx", file, dryRun: false, CancellationToken.None);

        Assert.Equal(1, detail.Batch.SuccessCount);
        var order = Assert.Single(h.Db.Context.TransportOrders.Where(o => o.TenantId == h.TenantId));
        Assert.Equal("AC-1001", order.CustomerReference);
        Assert.Equal(4m, order.Quantity);
        Assert.Equal(1200m, order.WeightKg);
        var stop = Assert.Single(h.Db.Context.Set<Modules.Orders.Entities.TransportOrderStop>()
            .Where(s => s.TenantId == h.TenantId));
        Assert.Equal("Gent", stop.City);
        Assert.Equal("9000", stop.PostalCode);
    }

    [Fact]
    public async Task CustomerBoundProfile_IsRefusedForAnotherCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profile = await h.Sut.CreateProfileAsync(AtlasProfileRequest(h.CustomerId), CancellationToken.None);
        var file = BuildAtlasWorkbook(["AC-1001", "9000", "Gent", "4", "1200", "Compressoren", null]);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ImportAsync(profile.Id, h.OtherCustomerId, "atlas.xlsx", file, dryRun: true, CancellationToken.None));
        Assert.Contains("andere klant", ex.Message);
    }

    [Fact]
    public async Task Analyze_SuggestsFieldsDeterministically_WithSamplesAndConfidence()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var file = BuildAtlasWorkbook(
            ["AC-1001", "9000", "Gent", "4", "1200", "Compressoren", "x"],
            ["AC-1002", "2800", "Mechelen", "2", "800", "Pompen", null]);

        var analysis = await h.Sut.AnalyzeAsync(file, CancellationToken.None);

        Assert.Equal(7, analysis.Columns.Count);
        var reference = analysis.Columns[0];
        Assert.Equal("Reference", reference.Header);
        Assert.Equal("customerReference", reference.SuggestedField);
        Assert.Equal(OrderImportFields.HighConfidence, reference.Confidence);
        Assert.Equal(["AC-1001", "AC-1002"], reference.SampleValues);
        Assert.Equal("unloadingPostalCode", analysis.Columns[1].SuggestedField);
        Assert.Equal("unloadingCity", analysis.Columns[2].SuggestedField);
        // "PAL" is an ambiguous abbreviation → medium confidence ("Controleren"), never silent.
        var pal = analysis.Columns[3];
        Assert.Equal("quantity", pal.SuggestedField);
        Assert.Equal(OrderImportFields.MediumConfidence, pal.Confidence);
        // A header the catalog does not know stays unmapped — no guessing.
        Assert.Equal("goodsDescription", analysis.Columns[5].SuggestedField);
        Assert.Null(analysis.Columns[6].SuggestedField)
;    }

    [Fact]
    public async Task Analyze_RanksSavedProfiles_ByHeaderOverlap()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateProfileAsync(AtlasProfileRequest(h.CustomerId), CancellationToken.None);
        // A second profile with disjoint headers must not match.
        await h.Sut.CreateProfileAsync(AtlasProfileRequest() with
        {
            Name = "Anders",
            CustomerId = null,
            SourceHeaders = ["Kolom X", "Kolom Y", "Kolom Z"],
        }, CancellationToken.None);
        var file = BuildAtlasWorkbook(["AC-1001", "9000", "Gent", "4", "1200", "Compressoren", null]);

        var analysis = await h.Sut.AnalyzeAsync(file, CancellationToken.None);

        var match = Assert.Single(analysis.ProfileMatches);
        Assert.Equal("Atlas Copco Orders", match.Name);
        Assert.Equal(h.CustomerId, match.CustomerId);
        Assert.Equal(100, match.MatchPercent);
    }

    [Fact]
    public async Task TenantIsolation_ProfilesNeverLeak_AndForeignCustomerBindingRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateProfileAsync(AtlasProfileRequest(h.CustomerId), CancellationToken.None);

        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now });
        await h.Db.Context.SaveChangesAsync();
        var otherTenant = new DevTenantContext(otherTenantId);
        var otherAudit = new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null));
        var otherSut = new OrderImportService(h.Db.Context, otherTenant, otherAudit,
            new TransportOrderService(h.Db.Context, otherTenant, otherAudit, new TestClock(Now)));

        Assert.DoesNotContain(await otherSut.ListProfilesAsync(CancellationToken.None, includeInactive: true),
            p => p.Name == "Atlas Copco Orders");
        // Binding a profile to tenant A's customer from tenant B is refused outright.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            otherSut.CreateProfileAsync(AtlasProfileRequest(h.CustomerId) with { Name = "Steel" }, CancellationToken.None));
    }
}
