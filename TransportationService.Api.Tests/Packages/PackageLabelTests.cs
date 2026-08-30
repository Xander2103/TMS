using System.Text.Json;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Labels;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

public class Code128EncoderTests
{
    [Fact]
    public void KnownVector_HasCorrectChecksum()
    {
        // "ABC": start B (104) + A(33) B(34) C(35); checksum = (104 + 33*1 + 34*2 + 35*3) % 103 = 310 % 103 = 1.
        var symbols = Code128Encoder.Encode("ABC");

        Assert.NotNull(symbols);
        Assert.Equal(new[] { 104, 33, 34, 35, 1, 106 }, symbols);
    }

    [Fact]
    public void ModuleWidths_SumMatchesSymbolCount()
    {
        var widths = Code128Encoder.ModuleWidths("PKG-00001-7K2M9QX4");

        Assert.NotNull(widths);
        // Every symbol contributes 11 modules; the stop contributes 13.
        var symbols = Code128Encoder.Encode("PKG-00001-7K2M9QX4")!;
        Assert.Equal((symbols.Count - 1) * 11 + 13, widths.Sum());
    }

    [Theory]
    [InlineData("")]
    [InlineData("héllo")] // non-ASCII
    public void UnencodableContent_ReturnsNull(string content) =>
        Assert.Null(Code128Encoder.Encode(content));
}

public class PackageLabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, PackageLabelService Sut, PackageService Packages,
        Guid TenantId, Guid OrderId, Guid CustomerId);

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
            TradingName = "Acme Transport",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 22), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
            Stops =
            [
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, City = "Rotterdam" },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, user);
        var barcodes = new PackageBarcodeService(db.Context, tenant, user, clock);
        var events = new PackageEventWriter(db.Context, tenant, user, clock);
        var sut = new PackageLabelService(db.Context, tenant, user, audit, new LabelRenderService(), events, clock);
        var packages = new PackageService(db.Context, tenant, audit, barcodes, events);
        return new Harness(db, sut, packages, tenantId, orderId, customerId);
    }

    [Fact]
    public async Task Print_ProducesPdf_Snapshot_AndLabelledTransition()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var package = (await h.Packages.CreateAsync(h.OrderId, new CreatePackageRequest("Doos"), CancellationToken.None)).Package!;

        var (pdf, error) = await h.Sut.PrintAsync([package.Id], LabelFormat.Thermal100x150, null, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(pdf);
        Assert.Equal((byte)'%', pdf![0]); // %PDF magic
        Assert.Equal((byte)'P', pdf[1]);

        var label = h.Db.Context.PackageLabels.Single();
        Assert.Equal(1, label.Version);
        Assert.Null(label.ReprintReason);
        var snapshot = JsonSerializer.Deserialize<LabelSnapshot>(label.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Acme Transport", snapshot.TenantName);
        Assert.Equal("Haven BV", snapshot.CustomerName);
        Assert.Equal("Rotterdam", snapshot.DeliveryLocation);

        Assert.Equal(PackageLifecycleStatus.Labelled,
            h.Db.Context.Packages.Single(p => p.Id == package.Id).CurrentLifecycleStatus);
    }

    [Fact]
    public async Task Reprint_CreatesNewVersion_WithReason_AndHistoricalSnapshotStaysFrozen()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var package = (await h.Packages.CreateAsync(h.OrderId, new CreatePackageRequest("Doos"), CancellationToken.None)).Package!;
        await h.Sut.PrintAsync([package.Id], LabelFormat.Thermal100x150, null, CancellationToken.None);

        // Customer data changes AFTER the first label…
        h.Db.Context.Customers.Single(c => c.Id == h.CustomerId).Name = "Haven BV — NIEUWE NAAM";
        await h.Db.Context.SaveChangesAsync();

        var (pdf, _) = await h.Sut.PrintAsync([package.Id], LabelFormat.A4, "Etiket gescheurd", CancellationToken.None);
        Assert.NotNull(pdf);

        var versions = await h.Sut.ListVersionsAsync(package.Id, CancellationToken.None);
        Assert.Equal(2, versions.Count);
        Assert.Equal("Etiket gescheurd", versions[0].ReprintReason);

        // v1 regenerates from ITS OWN snapshot: the old customer name, untouched.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var v1 = h.Db.Context.PackageLabels.Single(l => l.Version == 1);
        var v2 = h.Db.Context.PackageLabels.Single(l => l.Version == 2);
        Assert.Equal("Haven BV", JsonSerializer.Deserialize<LabelSnapshot>(v1.SnapshotJson, options)!.CustomerName);
        Assert.Equal("Haven BV — NIEUWE NAAM", JsonSerializer.Deserialize<LabelSnapshot>(v2.SnapshotJson, options)!.CustomerName);
        Assert.NotNull(await h.Sut.RenderVersionAsync(package.Id, 1, CancellationToken.None));

        // Custody: Labelled once + LabelReprinted once.
        var events = h.Db.Context.PackageEvents.Where(e => e.PackageId == package.Id).Select(e => e.EventType).ToList();
        Assert.Contains(PackageEventType.Labelled, events);
        Assert.Contains(PackageEventType.LabelReprinted, events);
    }

    [Fact]
    public async Task BulkPrint_A4_PaginatesEightPerPage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var packages = new List<Guid>();
        for (var i = 0; i < 9; i += 1)
        {
            packages.Add((await h.Packages.CreateAsync(h.OrderId, new CreatePackageRequest($"Doos {i + 1}"), CancellationToken.None)).Package!.Id);
        }

        var (pdf, error) = await h.Sut.PrintAsync(packages, LabelFormat.A4, null, CancellationToken.None);

        Assert.Null(error);
        // 9 labels at 8 per A4 page → exactly 2 pages.
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf!), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public async Task Snapshot_CarriesSequenceLabel_WithinTheOrder_AndDutchUnitType()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = (await h.Packages.CreateAsync(h.OrderId,
            new CreatePackageRequest("Pallet A") { UnitType = PackageUnitType.EuroPallet }, CancellationToken.None)).Package!;
        var second = (await h.Packages.CreateAsync(h.OrderId,
            new CreatePackageRequest("Pallet B") { UnitType = PackageUnitType.EuroPallet }, CancellationToken.None)).Package!;

        await h.Sut.PrintAsync([first.Id, second.Id], LabelFormat.Thermal100x150, null, CancellationToken.None);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshots = h.Db.Context.PackageLabels.ToList()
            .Select(l => (l.PackageId, Snapshot: JsonSerializer.Deserialize<LabelSnapshot>(l.SnapshotJson, options)!))
            .ToDictionary(x => x.PackageId, x => x.Snapshot);

        Assert.Equal("Collo 1 van 2", snapshots[first.Id].SequenceLabel);
        Assert.Equal("Collo 2 van 2", snapshots[second.Id].SequenceLabel);
        Assert.Equal("Europallet", snapshots[first.Id].UnitTypeLabel);
    }

    /// <summary>
    /// Wave 1 fix A (A5): a pin pointing at a stop that no longer exists — every order edited
    /// before blocker C-01 landed carries some — used to resolve to null through the
    /// soft-delete-filtered stop set and print a BLANK sender/recipient. A dangling pin now falls
    /// back to the order's own stops, exactly like the null pin the entity documents.
    /// </summary>
    [Fact]
    public async Task Snapshot_WithAPinToARemovedStop_FallsBackToTheOrdersLiveStops()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var removedStopId = Guid.NewGuid();
        h.Db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = removedStopId, TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 3,
            StopType = StopType.Unloading, City = "Gent", IsDeleted = true,
        });
        var package = (await h.Packages.CreateAsync(h.OrderId, new CreatePackageRequest("Doos"), CancellationToken.None)).Package!;
        h.Db.Context.Packages.Single(p => p.Id == package.Id).DeliveryStopId = removedStopId;
        await h.Db.Context.SaveChangesAsync();

        var (pdf, error) = await h.Sut.PrintAsync([package.Id], LabelFormat.Thermal100x150, null, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(pdf);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = JsonSerializer.Deserialize<LabelSnapshot>(
            h.Db.Context.PackageLabels.Single(l => l.PackageId == package.Id).SnapshotJson, options)!;
        Assert.Equal("Rotterdam", snapshot.DeliveryLocation);
        Assert.Equal("Rotterdam", snapshot.RecipientPostalCodeCity);
        Assert.Equal("Antwerpen", snapshot.LoadingLocation);
    }

    [Fact]
    public async Task CancelledPackages_GetNoLabel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var package = (await h.Packages.CreateAsync(h.OrderId, new CreatePackageRequest("Doos"), CancellationToken.None)).Package!;
        await h.Packages.CancelAsync(package.Id, new CancelPackageRequest("weg"), CancellationToken.None);

        var (pdf, error) = await h.Sut.PrintAsync([package.Id], LabelFormat.Thermal100x150, null, CancellationToken.None);

        Assert.Null(pdf);
        Assert.NotNull(error);
    }
}
