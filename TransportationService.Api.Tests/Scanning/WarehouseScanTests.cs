using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Scanning.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Scanning;

/// <summary>
/// Wave 4 §3: the trip-less warehouse entry point of the one scan pipeline. Received registers
/// arrival, Moved/Staged stamp custody+location without lifecycle changes, Return checks a
/// failed package in as ReturnedToDepot without a return trip, unknown barcodes become warning
/// LEDGER rows (never dropped) and the ClientEventId replay contract matches the trip flow.
/// </summary>
public class WarehouseScanTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 15, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, WarehouseScanService Sut,
        Guid TenantId, Guid PackageId, Guid ZoneId, Guid PositionId);

    private static async Task<Harness> SeedAsync(
        PackageLifecycleStatus initialStatus = PackageLifecycleStatus.Labelled)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var masterLocationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new DateOnly(2026, 8, 12),
        });
        db.Context.Packages.Add(new Package
        {
            Id = packageId, TenantId = tenantId, TransportOrderId = orderId,
            PackageNumber = "PKG-00001", BarcodeValue = "PKG-00001-AAAA",
            Description = "Doos", CurrentLifecycleStatus = initialStatus,
        });
        db.Context.PackageBarcodes.Add(new PackageBarcode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageId = packageId,
            Value = "PKG-00001-AAAA", Type = PackageBarcodeType.Code128, IsActive = true,
        });
        db.Context.Locations.Add(new Location { Id = masterLocationId, TenantId = tenantId, Name = "Depot", City = "Antwerpen", IsActive = true });
        db.Context.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = tenantId, Name = "Magazijn A", LocationId = masterLocationId, IsActive = true });
        db.Context.WarehouseLocations.AddRange(
            new WarehouseLocation { Id = zoneId, TenantId = tenantId, WarehouseId = warehouseId, Code = "A", Name = "Zone A", Kind = "Zone", IsActive = true },
            new WarehouseLocation { Id = positionId, TenantId = tenantId, WarehouseId = warehouseId, ParentId = zoneId, Code = "A-01", Name = "Positie", Kind = "Position", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var clock = new TestClock(Now);
        var barcodes = new PackageBarcodeService(db.Context, tenant, currentUser, clock);
        var writer = new PackageEventWriter(db.Context, tenant, currentUser, clock);
        var sut = new WarehouseScanService(db.Context, tenant, currentUser, barcodes, writer, clock);
        return new Harness(db, sut, tenantId, packageId, zoneId, positionId);
    }

    private static Task<WarehouseScanFeedbackDto> ScanAsync(
        Harness h, ScanType type, Guid? locationId = null, Guid? clientEventId = null, string barcode = "PKG-00001-AAAA") =>
        h.Sut.SubmitAsync(new WarehouseScanRequest(barcode, type, locationId, clientEventId), CancellationToken.None);

    [Fact]
    public async Task Received_RegistersArrival_SetsLocation_AndAppendsCustody()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var feedback = await ScanAsync(h, ScanType.Received, h.ZoneId);

        Assert.Equal("Received", feedback.Outcome);
        Assert.Equal(ScanFeedbackLevel.Success, feedback.Level);
        var package = await h.Db.Context.Packages.SingleAsync(p => p.Id == h.PackageId);
        Assert.Equal(PackageLifecycleStatus.AwaitingLoading, package.CurrentLifecycleStatus);
        Assert.Equal(h.ZoneId, package.CurrentWarehouseLocationId);
        var custody = await h.Db.Context.PackageEvents
            .SingleAsync(e => e.PackageId == h.PackageId && e.EventType == PackageEventType.Received);
        Assert.Equal(h.ZoneId, custody.WarehouseLocationId);
        var ledger = await h.Db.Context.ScanEvents.SingleAsync(e => e.PackageId == h.PackageId);
        Assert.Null(ledger.TripId);
        Assert.Equal(ScanType.Received, ledger.ScanType);
    }

    [Fact]
    public async Task Moved_RequiresALocation_UpdatesTheProjection_NoLifecycleChange()
    {
        var h = await SeedAsync(PackageLifecycleStatus.AwaitingLoading);
        using var _ = h.Db;

        var missing = await ScanAsync(h, ScanType.Moved);
        Assert.Equal("LocationRequired", missing.Outcome);
        Assert.Equal(ScanFeedbackLevel.Error, missing.Level);

        var moved = await ScanAsync(h, ScanType.Moved, h.PositionId);
        Assert.Equal("Moved", moved.Outcome);
        var package = await h.Db.Context.Packages.SingleAsync(p => p.Id == h.PackageId);
        Assert.Equal(PackageLifecycleStatus.AwaitingLoading, package.CurrentLifecycleStatus);
        Assert.Equal(h.PositionId, package.CurrentWarehouseLocationId);
        Assert.Equal(1, await h.Db.Context.PackageEvents
            .CountAsync(e => e.PackageId == h.PackageId && e.EventType == PackageEventType.MovedLocation));
    }

    [Fact]
    public async Task Return_ChecksAFailedPackageIn_WithoutAnyTrip()
    {
        var h = await SeedAsync(PackageLifecycleStatus.DeliveryFailed);
        using var _ = h.Db;

        var feedback = await ScanAsync(h, ScanType.Return, h.ZoneId);

        Assert.Equal("ReturnedToDepot", feedback.Outcome);
        var package = await h.Db.Context.Packages.SingleAsync(p => p.Id == h.PackageId);
        Assert.Equal(PackageLifecycleStatus.ReturnedToDepot, package.CurrentLifecycleStatus);
        Assert.Equal(h.ZoneId, package.CurrentWarehouseLocationId);
    }

    [Fact]
    public async Task UnknownBarcode_BecomesAWarningLedgerRow_NeverDropped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var feedback = await ScanAsync(h, ScanType.Received, barcode: "ONBEKEND-123");

        Assert.Equal("UnknownBarcode", feedback.Outcome);
        Assert.Equal(ScanFeedbackLevel.Warning, feedback.Level);
        var row = await h.Db.Context.ScanEvents.SingleAsync(e => e.Barcode == "ONBEKEND-123");
        Assert.Null(row.TransportOrderId);
        Assert.Null(row.PackageId);
        Assert.Equal(ScanResult.UnexpectedItem, row.Result);
    }

    [Fact]
    public async Task Replay_WithTheSameClientEventId_NeverWritesASecondRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var key = Guid.NewGuid();

        var first = await ScanAsync(h, ScanType.Received, h.ZoneId, key);
        var replay = await ScanAsync(h, ScanType.Received, h.ZoneId, key);

        Assert.Equal("Received", first.Outcome);
        Assert.Equal("Received", replay.Outcome);
        Assert.Equal(1, await h.Db.Context.ScanEvents.CountAsync(e => e.ClientEventId == key));
        Assert.Equal(1, await h.Db.Context.PackageEvents
            .CountAsync(e => e.PackageId == h.PackageId && e.EventType == PackageEventType.Received));
    }

    [Fact]
    public async Task DeliveredPackage_OnReceivedScan_WarnsButStaysRecorded()
    {
        var h = await SeedAsync(PackageLifecycleStatus.Delivered);
        using var _ = h.Db;

        var feedback = await ScanAsync(h, ScanType.Received, h.ZoneId);

        Assert.Equal("UnexpectedStatus", feedback.Outcome);
        Assert.Equal(ScanFeedbackLevel.Warning, feedback.Level);
        var package = await h.Db.Context.Packages.SingleAsync(p => p.Id == h.PackageId);
        Assert.Equal(PackageLifecycleStatus.Delivered, package.CurrentLifecycleStatus); // untouched
        Assert.Equal(1, await h.Db.Context.ScanEvents.CountAsync(e => e.PackageId == h.PackageId));
    }
}
