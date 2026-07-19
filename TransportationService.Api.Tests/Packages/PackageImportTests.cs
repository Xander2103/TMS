using ClosedXML.Excel;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

public class PackageImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, PackageImportService Sut, PackageService Packages, Guid TenantId, Guid OrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 22), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, user);
        var barcodes = new PackageBarcodeService(db.Context, tenant, user, clock);
        var events = new PackageEventWriter(db.Context, tenant, user, clock);
        var sut = new PackageImportService(db.Context, tenant, audit, barcodes, events);
        var packages = new PackageService(db.Context, tenant, audit, barcodes, events);
        return new Harness(db, sut, packages, tenantId, orderId);
    }

    /// <summary>Builds an import workbook; each row = 17 columns matching the template.</summary>
    private static MemoryStream Workbook(params string?[][] rows)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Colli");
        for (var c = 1; c <= 17; c += 1)
        {
            sheet.Cell(1, c).SetValue($"Kolom{c}");
        }

        for (var r = 0; r < rows.Length; r += 1)
        {
            for (var c = 0; c < rows[r].Length; c += 1)
            {
                if (rows[r][c] is { } value)
                {
                    sheet.Cell(r + 2, c + 1).SetValue(value);
                }
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static string?[] Row(string? number, string? description, string? barcode = null, string? quantity = null) =>
        [number, description, quantity, null, barcode, null, null, null, null, null, null, null, null, null, null, null, null];

    [Fact]
    public async Task BulkCreate_GroupsOnPallet_WithSequentialReferences()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var (result, error) = await h.Packages.BulkCreateAsync(h.OrderId, new BulkCreatePackagesRequest(
            Count: 3, Description: "Dozen bouten", ReferencePrefix: "REF", GroupOnPallet: true,
            WeightKg: 5m), CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(3, result!.Packages.Count);
        Assert.NotNull(result.Pallet);
        Assert.Equal(PackageUnitType.Pallet, result.Pallet!.UnitType);
        Assert.All(result.Packages, p => Assert.Equal(result.Pallet.Id, p.ParentPackageId));
        Assert.Equal(new[] { "REF-1", "REF-2", "REF-3" }, result.Packages.Select(p => p.CustomerReference));
        // Pallet + 3 children claim 4 contiguous numbers.
        var numbers = h.Db.Context.Packages.Select(p => p.PackageNumber).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "PKG-00001", "PKG-00002", "PKG-00003", "PKG-00004" }, numbers);
    }

    [Fact]
    public async Task Import_Preview_ClassifiesCreatesAndErrors()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        using var stream = Workbook(
            Row(null, "Doos A", "EXT-1"),
            Row(null, null, quantity: "2"),                    // missing description
            Row(null, "Doos B", "EXT-1"));      // duplicate barcode in file

        var (preview, error) = await h.Sut.PreviewAsync(h.OrderId, stream, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(3, preview!.TotalRows);
        Assert.Equal(1, preview.Creates);
        Assert.Equal(2, preview.Errors);
        Assert.Contains(preview.Rows, r => r.Messages.Any(m => m.Contains("meermaals")));
    }

    [Fact]
    public async Task Import_AllOrNothing_AbortsOnAnyError_NothingCommitted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        using var stream = Workbook(Row(null, "Goed"), Row(null, null, quantity: "2"));

        var (result, error) = await h.Sut.CommitAsync(h.OrderId, stream, allOrNothing: true, allowUpdates: false, CancellationToken.None);

        Assert.Null(error);
        Assert.False(result!.Committed);
        Assert.Equal(0, result.Created);
        Assert.NotNull(result.ErrorWorkbookBase64);
        Assert.Empty(h.Db.Context.Packages.ToList());
    }

    [Fact]
    public async Task Import_PartialMode_CommitsValidRows_AndReportsFailures()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        using var stream = Workbook(Row(null, "Goed", "EXT-OK"), Row(null, null, quantity: "2"));

        var (result, _) = await h.Sut.CommitAsync(h.OrderId, stream, allOrNothing: false, allowUpdates: false, CancellationToken.None);

        Assert.True(result!.Committed);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Failed);
        var package = Assert.Single(h.Db.Context.Packages.ToList());
        Assert.Equal("Goed", package.Description);
        Assert.Equal("EXT-OK", package.ExternalBarcode);
        // Import creations get a custody event referencing the source row.
        Assert.Contains(h.Db.Context.PackageEvents.ToList(),
            e => e.PackageId == package.Id && e.Notes!.Contains("rij 2"));
    }

    [Fact]
    public async Task Import_DetectsExistingBarcodes_AndRefusesAmbiguousUpdates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var existing = await h.Packages.CreateAsync(h.OrderId,
            new CreatePackageRequest("Bestaand") with { ExternalBarcode = "TAKEN-1" }, CancellationToken.None);

        using var duplicateStream = Workbook(Row(null, "Nieuw", "TAKEN-1"));
        var (duplicatePreview, _) = await h.Sut.PreviewAsync(h.OrderId, duplicateStream, CancellationToken.None);
        Assert.Equal(1, duplicatePreview!.Errors);
        Assert.Contains(duplicatePreview.Rows[0].Messages, m => m.Contains("al in gebruik"));

        // Updating an existing package may never silently change its barcode.
        using var updateStream = Workbook(Row(existing.Package!.PackageNumber, "Bestaand v2", "OTHER-9"));
        var (updateResult, _) = await h.Sut.CommitAsync(h.OrderId, updateStream, allOrNothing: false, allowUpdates: true, CancellationToken.None);
        Assert.Equal(1, updateResult!.Failed);
        Assert.Contains(updateResult.Rows[0].Messages, m => m.Contains("heretiketteren"));

        // A clean update (no barcode column) works and only touches supplied fields.
        using var cleanStream = Workbook(Row(existing.Package.PackageNumber, "Bestaand v2"));
        var (cleanResult, _) = await h.Sut.CommitAsync(h.OrderId, cleanStream, allOrNothing: true, allowUpdates: true, CancellationToken.None);
        Assert.True(cleanResult!.Committed);
        Assert.Equal(1, cleanResult.Updated);
        Assert.Equal("Bestaand v2", h.Db.Context.Packages.Single(p => p.Id == existing.Package.Id).Description);
        Assert.Equal("TAKEN-1", h.Db.Context.Packages.Single(p => p.Id == existing.Package.Id).ExternalBarcode);
    }

    [Fact]
    public async Task ErrorWorkbook_WritesFormulaPayloadsAsText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        using var stream = Workbook(Row(null, "=HYPERLINK(\"http://evil\",\"x\")", quantity: "abc"));

        var (result, _) = await h.Sut.CommitAsync(h.OrderId, stream, allOrNothing: true, allowUpdates: false, CancellationToken.None);

        Assert.NotNull(result!.ErrorWorkbookBase64);
        using var errorWorkbook = new XLWorkbook(new MemoryStream(Convert.FromBase64String(result.ErrorWorkbookBase64!)));
        var cell = errorWorkbook.Worksheet("Fouten").Cell(2, 3); // Omschrijving column (offset by Rij)
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.False(cell.HasFormula);
        Assert.StartsWith("=HYPERLINK", cell.GetString());
    }

    [Fact]
    public async Task Import_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var audit = new AuditService(h.Db.Context, foreignTenant, user);
        var foreign = new PackageImportService(h.Db.Context, foreignTenant, audit,
            new PackageBarcodeService(h.Db.Context, foreignTenant, user, clock),
            new PackageEventWriter(h.Db.Context, foreignTenant, user, clock));

        using var stream = Workbook(Row(null, "Spion"));
        var (_, error) = await foreign.PreviewAsync(h.OrderId, stream, CancellationToken.None);

        Assert.NotNull(error); // order invisible across tenants
    }
}
