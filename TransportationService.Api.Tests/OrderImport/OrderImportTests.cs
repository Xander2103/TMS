using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.OrderImport.Entities;
using TransportationService.Api.Modules.OrderImport.Services;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.OrderImport;

/// <summary>
/// P13 Excel order import: dry-run validation, real runs through the regular order service
/// (wrapper dossiers included — the inbound-normalization guarantee), row error isolation,
/// checksum duplicate refusal and per-row external-reference dedupe.
/// </summary>
public class OrderImportTests
{
    private static readonly DateTime Now = new(2026, 08, 12, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, OrderImportService Sut, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now));
        var sut = new OrderImportService(db.Context, tenant, audit, orders);
        return new Harness(db, sut, tenantId, customerId);
    }

    /// <summary>Lists profiles (triggering the lazy seed) and returns the seeded sample profile id.</summary>
    private static async Task<Guid> SampleProfileIdAsync(Harness h)
    {
        var profiles = await h.Sut.ListProfilesAsync(CancellationToken.None);
        return Assert.Single(profiles, p => p.Name == "Generiek v1").Id;
    }

    /// <summary>
    /// Builds an .xlsx laid out per the "Generiek v1" sample mapping (A=referentie, B=datum,
    /// C=goederen, D=hoeveelheid, E=eenheid, F=gewicht, G-J=laadadres, K-N=losadres, O=ADR),
    /// with one header row.
    /// </summary>
    private static byte[] BuildWorkbook(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Opdrachten");
        string[] headers =
        [
            "Referentie", "Datum", "Goederen", "Hoeveelheid", "Eenheid", "Gewicht (kg)",
            "Laadlocatie", "Laad postcode", "Laad gemeente", "Laad land",
            "Loslocatie", "Los postcode", "Los gemeente", "Los land", "ADR",
        ];
        for (var column = 0; column < headers.Length; column += 1)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (var index = 0; index < rows.Length; index += 1)
        {
            var row = rows[index];
            for (var column = 0; column < row.Length; column += 1)
            {
                switch (row[column])
                {
                    case null:
                        break;
                    case decimal number:
                        sheet.Cell(index + 2, column + 1).SetValue(number);
                        break;
                    case int number:
                        sheet.Cell(index + 2, column + 1).SetValue(number);
                        break;
                    default:
                        sheet.Cell(index + 2, column + 1).SetValue(row[column]!.ToString());
                        break;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static object?[] ValidRow(string reference, string goods = "Paletten bouwmateriaal") =>
        [reference, "12/08/2026", goods, 10, "PAL", 1200, "Magazijn Noord", "2030", "Antwerpen", "BE", "Werf Zuid", "9000", "Gent", "BE", "nee"];

    // ------------------------------------------------------------- dry run

    [Fact]
    public async Task DryRun_ValidatesRows_WithoutCreatingOrders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profileId = await SampleProfileIdAsync(h);
        var file = BuildWorkbook(ValidRow("REF-1"), ValidRow("REF-2"));

        var detail = await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: true, CancellationToken.None);

        Assert.Equal(OrderImportBatchStatus.Validated, detail.Batch.Status);
        Assert.True(detail.Batch.DryRun);
        Assert.Equal(2, detail.Batch.RowCount);
        Assert.Equal(2, detail.Batch.SuccessCount);
        Assert.Equal(0, detail.Batch.FailureCount);
        Assert.All(detail.Rows, r => Assert.Null(r.CreatedTransportOrderId));
        Assert.Empty(h.Db.Context.TransportOrders);
        Assert.Empty(h.Db.Context.TransportDossiers);
    }

    // ------------------------------------------------------------- real run

    [Fact]
    public async Task RealRun_CreatesOrders_EachInsideAWrapperDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profileId = await SampleProfileIdAsync(h);
        var file = BuildWorkbook(ValidRow("REF-1"), ValidRow("REF-2"));

        var detail = await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: false, CancellationToken.None);

        Assert.Equal(OrderImportBatchStatus.Processed, detail.Batch.Status);
        Assert.Equal(2, detail.Batch.SuccessCount);
        Assert.Equal(0, detail.Batch.FailureCount);

        var orders = await h.Db.Context.TransportOrders.ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.All(detail.Rows, r =>
        {
            Assert.Equal(OrderImportRowStatus.Created, r.Status);
            Assert.NotNull(r.CreatedTransportOrderId);
        });

        // The inbound-normalization guarantee: every imported order lives in a dossier.
        foreach (var order in orders)
        {
            Assert.True(await h.Db.Context.Set<TransportationService.Api.Modules.Dossiers.Entities.DossierOrder>()
                .AnyAsync(d => d.TransportOrderId == order.Id));
        }

        var withStops = await h.Db.Context.TransportOrders.Include(o => o.Stops).ToListAsync();
        Assert.All(withStops, o => Assert.Equal(2, o.Stops.Count));
        Assert.Contains(withStops, o => o.CustomerReference == "REF-1");

        // Batch audit trail exists.
        Assert.Single(h.Db.Context.AuditLogs.Where(a => a.EntityType == "OrderImportBatch" && a.Action == "Processed"));
    }

    // ------------------------------------------------------------- row isolation

    [Fact]
    public async Task RealRun_BadRow_DoesNotAbortTheBatch()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profileId = await SampleProfileIdAsync(h);
        // Middle row misses both the unloading city/location AND any cargo info.
        object?[] badRow = ["REF-BAD", null, null, null, null, null, null, null, null, null, null, null, null, null, null];
        var file = BuildWorkbook(ValidRow("REF-1"), badRow, ValidRow("REF-3"));

        var detail = await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: false, CancellationToken.None);

        Assert.Equal(OrderImportBatchStatus.Processed, detail.Batch.Status);
        Assert.Equal(3, detail.Batch.RowCount);
        Assert.Equal(2, detail.Batch.SuccessCount);
        Assert.Equal(1, detail.Batch.FailureCount);

        var errorRow = Assert.Single(detail.Rows, r => r.Status == OrderImportRowStatus.Error);
        Assert.Equal(3, errorRow.RowNumber); // spreadsheet row 3 (header + row 2)
        Assert.Contains("losplaats", errorRow.Error);

        Assert.Equal(2, await h.Db.Context.TransportOrders.CountAsync());
    }

    // ------------------------------------------------------------- checksum dedupe

    [Fact]
    public async Task RealRun_SameChecksumAlreadyProcessed_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profileId = await SampleProfileIdAsync(h);
        var file = BuildWorkbook(ValidRow("REF-1"));

        await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: false, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: false, CancellationToken.None));
        Assert.Equal("Dit bestand werd al verwerkt.", exception.Message);

        // A dry run of the same file stays possible (validation is harmless).
        var dryDetail = await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: true, CancellationToken.None);
        Assert.Equal(OrderImportBatchStatus.Validated, dryDetail.Batch.Status);
    }

    [Fact]
    public async Task RealRun_AfterDryRunOfSameFile_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profileId = await SampleProfileIdAsync(h);
        var file = BuildWorkbook(ValidRow("REF-1"));

        await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: true, CancellationToken.None);
        var detail = await h.Sut.ImportAsync(profileId, h.CustomerId, "orders.xlsx", file, dryRun: false, CancellationToken.None);

        Assert.Equal(OrderImportBatchStatus.Processed, detail.Batch.Status);
        Assert.Equal(1, detail.Batch.SuccessCount);
    }

    // ------------------------------------------------------------- external-ref dedupe

    [Fact]
    public async Task RealRun_ExistingCustomerReference_SkipsTheRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var profileId = await SampleProfileIdAsync(h);

        // First import creates REF-1; the second file (different bytes) re-sends REF-1 plus a new REF-2.
        await h.Sut.ImportAsync(profileId, h.CustomerId, "eerste.xlsx",
            BuildWorkbook(ValidRow("REF-1")), dryRun: false, CancellationToken.None);
        var detail = await h.Sut.ImportAsync(profileId, h.CustomerId, "tweede.xlsx",
            BuildWorkbook(ValidRow("REF-1", "Andere goederen"), ValidRow("REF-2")), dryRun: false, CancellationToken.None);

        var skipped = Assert.Single(detail.Rows, r => r.Status == OrderImportRowStatus.Skipped);
        Assert.Equal("Bestaat al (referentie).", skipped.Error);
        Assert.Equal("REF-1", skipped.ExternalReference);
        Assert.Equal(1, detail.Batch.SuccessCount);

        // REF-1 exists exactly once; REF-2 was created.
        Assert.Equal(1, await h.Db.Context.TransportOrders.CountAsync(o => o.CustomerReference == "REF-1"));
        Assert.Equal(1, await h.Db.Context.TransportOrders.CountAsync(o => o.CustomerReference == "REF-2"));
    }

    // ------------------------------------------------------------- profile seed idempotency

    [Fact]
    public async Task ListProfiles_SeedsTheSampleProfileOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var first = await h.Sut.ListProfilesAsync(CancellationToken.None);
        var second = await h.Sut.ListProfilesAsync(CancellationToken.None);

        Assert.Single(first, p => p.Name == "Generiek v1");
        Assert.Single(second, p => p.Name == "Generiek v1");
        Assert.Equal(1, await h.Db.Context.OrderImportProfiles.CountAsync());
    }
}
