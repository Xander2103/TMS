using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Scanning.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Modules.Warehousing.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Warehousing;

/// <summary>
/// Wave 5: the movement-based storage clock. A Received/Return scan OPENS a stay (at most one
/// open per package — reopening only refreshes the location), any leave-type custody event
/// CLOSES it centrally via the StorageClockInterceptor, and the billing derivation counts
/// started pallet-days per period with open stays running until the period end.
/// </summary>
public class StorageClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 16, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, WarehouseScanService Scans, PackageEventWriter Writer, StorageBillingService Billing,
        Guid TenantId, Guid CustomerId, Guid OrderId, Guid PackageId, Guid WarehouseId, Guid ZoneId);

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
            CurrentLifecycleStatus = initialStatus,
        });
        db.Context.PackageBarcodes.Add(new PackageBarcode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageId = packageId,
            Value = "PKG-00001-AAAA", Type = PackageBarcodeType.Code128, IsActive = true,
        });
        db.Context.Locations.Add(new Location { Id = masterLocationId, TenantId = tenantId, Name = "Depot", City = "Antwerpen", IsActive = true });
        db.Context.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = tenantId, Name = "Magazijn A", LocationId = masterLocationId, IsActive = true });
        db.Context.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = zoneId, TenantId = tenantId, WarehouseId = warehouseId, Code = "A", Name = "Zone A", Kind = "Zone", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var clock = new TestClock(Now);
        var writer = new PackageEventWriter(db.Context, tenant, currentUser, clock);
        var scans = new WarehouseScanService(db.Context, tenant, currentUser,
            new PackageBarcodeService(db.Context, tenant, currentUser, clock), writer, clock);
        var billing = new StorageBillingService(db.Context, tenant);
        return new Harness(db, scans, writer, billing, tenantId, customerId, orderId, packageId, warehouseId, zoneId);
    }

    [Fact]
    public async Task ReceivedScan_OpensAStay_ReceivedAgain_OnlyRefreshesIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Received, WarehouseId: h.WarehouseId), CancellationToken.None);
        var stay = await h.Db.Context.StorageStays.SingleAsync(s => s.PackageId == h.PackageId);
        Assert.Null(stay.OutAt);
        Assert.Equal(h.WarehouseId, stay.WarehouseId);
        Assert.Null(stay.WarehouseLocationId);

        // Second Received with a location: still ONE stay, location refreshed.
        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Received, h.ZoneId), CancellationToken.None);
        var stays = await h.Db.Context.StorageStays.Where(s => s.PackageId == h.PackageId).ToListAsync();
        Assert.Single(stays);
        Assert.Equal(h.ZoneId, stays[0].WarehouseLocationId);
    }

    [Fact]
    public async Task LeaveCustodyEvent_ClosesTheOpenStay_ViaTheInterceptor()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Received, h.ZoneId), CancellationToken.None);

        // A load scan staged anywhere in the codebase closes the stay in the same save.
        var package = await h.Db.Context.Packages.SingleAsync(p => p.Id == h.PackageId);
        var leave = h.Writer.Stage(package, PackageEventType.LoadScan,
            PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded);
        package.CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
        await h.Db.Context.SaveChangesAsync();

        var stay = await h.Db.Context.StorageStays.SingleAsync(s => s.PackageId == h.PackageId);
        Assert.NotNull(stay.OutAt);
        Assert.Equal(leave.Id, stay.OutPackageEventId);

        // A LATER return check-in opens a fresh stay — history is never rewritten.
        package.CurrentLifecycleStatus = PackageLifecycleStatus.DeliveryFailed;
        await h.Db.Context.SaveChangesAsync();
        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Return, h.ZoneId), CancellationToken.None);
        Assert.Equal(2, await h.Db.Context.StorageStays.CountAsync(s => s.PackageId == h.PackageId));
    }

    [Fact]
    public async Task Billing_CountsStartedDays_OpenStaysRunToPeriodEnd()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Closed stay: 1.5 days → 2 started days. Open stay on a second package: 3 days to period end.
        var package2 = Guid.NewGuid();
        h.Db.Context.Packages.Add(new Package
        {
            Id = package2, TenantId = h.TenantId, TransportOrderId = h.OrderId,
            PackageNumber = "PKG-00002", BarcodeValue = "PKG-00002-BBBB",
        });
        h.Db.Context.StorageStays.AddRange(
            new StorageStay
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = h.PackageId, WarehouseId = h.WarehouseId,
                InAt = new DateTime(2026, 8, 3, 8, 0, 0), OutAt = new DateTime(2026, 8, 4, 20, 0, 0),
            },
            new StorageStay
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = package2, WarehouseId = h.WarehouseId,
                InAt = new DateTime(2026, 8, 8, 0, 0, 0), OutAt = null,
            });
        await h.Db.Context.SaveChangesAsync();

        var billing = await h.Billing.ComputeAsync(
            h.CustomerId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), CancellationToken.None);

        Assert.Equal(2m + 3m, billing.TotalPalletDays);
        Assert.Equal(1, billing.OpenStays);
        var order = Assert.Single(billing.PerOrder);
        Assert.Equal("ORD-0001", order.OrderNumber);
        Assert.Equal(2, order.PackageCount);
        var warehouse = Assert.Single(billing.PerWarehouse);
        Assert.Equal("Magazijn A", warehouse.WarehouseName);
    }

    [Fact]
    public async Task LocationProjection_FollowsTheFullCustodyChain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // IN: location set.
        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Received, h.ZoneId), CancellationToken.None);
        var package = await h.Db.Context.Packages.SingleAsync(p => p.Id == h.PackageId);
        Assert.Equal(h.ZoneId, package.CurrentWarehouseLocationId);

        // MOVE: location changes.
        var zoneB = Guid.NewGuid();
        h.Db.Context.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = zoneB, TenantId = h.TenantId, WarehouseId = h.WarehouseId, Code = "B", Name = "Zone B", Kind = "Zone", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Moved, zoneB), CancellationToken.None);
        await h.Db.Context.Entry(package).ReloadAsync();
        Assert.Equal(zoneB, package.CurrentWarehouseLocationId);

        // LOAD-OUT: physical departure clears the projection in the same save.
        h.Writer.Stage(package, PackageEventType.LoadScan,
            PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded);
        package.CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
        await h.Db.Context.SaveChangesAsync();
        await h.Db.Context.Entry(package).ReloadAsync();
        Assert.Null(package.CurrentWarehouseLocationId);

        // FAILED DELIVERY on the road: still no warehouse location — the goods are on the truck.
        package.CurrentLifecycleStatus = PackageLifecycleStatus.Refused;
        await h.Db.Context.SaveChangesAsync();
        await h.Db.Context.Entry(package).ReloadAsync();
        Assert.Null(package.CurrentWarehouseLocationId);

        // REAL RETURN SCAN: only now is the package physically back on a location.
        await h.Scans.SubmitAsync(new WarehouseScanRequest(
            "PKG-00001-AAAA", ScanType.Return, h.ZoneId), CancellationToken.None);
        await h.Db.Context.Entry(package).ReloadAsync();
        Assert.Equal(h.ZoneId, package.CurrentWarehouseLocationId);
        Assert.Equal(PackageLifecycleStatus.ReturnedToDepot, package.CurrentLifecycleStatus);
    }

    [Fact]
    public async Task PartialOutbound_ThroughTheScanPipeline_SplitsTheAccrual()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Five units scanned IN through the real pipeline.
        var packageIds = new List<Guid> { h.PackageId };
        for (var i = 2; i <= 5; i++)
        {
            var id = Guid.NewGuid();
            packageIds.Add(id);
            h.Db.Context.Packages.Add(new Package
            {
                Id = id, TenantId = h.TenantId, TransportOrderId = h.OrderId,
                PackageNumber = $"PKG-0000{i}", BarcodeValue = $"PKG-0000{i}-AAAA",
                CurrentLifecycleStatus = PackageLifecycleStatus.Labelled,
            });
            h.Db.Context.PackageBarcodes.Add(new PackageBarcode
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = id,
                Value = $"PKG-0000{i}-AAAA", Type = PackageBarcodeType.Code128, IsActive = true,
            });
        }
        await h.Db.Context.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
        {
            await h.Scans.SubmitAsync(new WarehouseScanRequest(
                $"PKG-0000{i}-AAAA", ScanType.Received, h.ZoneId), CancellationToken.None);
        }
        Assert.Equal(5, await h.Db.Context.StorageStays.CountAsync(s => s.OutAt == null));

        // Two of the five leave on a truck; their clocks stop, the other three keep running.
        foreach (var id in packageIds.Take(2))
        {
            var p = await h.Db.Context.Packages.SingleAsync(x => x.Id == id);
            h.Writer.Stage(p, PackageEventType.LoadScan,
                PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded);
            p.CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
        }
        await h.Db.Context.SaveChangesAsync();

        Assert.Equal(3, await h.Db.Context.StorageStays.CountAsync(s => s.OutAt == null));
        Assert.Equal(2, await h.Db.Context.StorageStays.CountAsync(s => s.OutAt != null));
        Assert.Equal(3, await h.Db.Context.Packages.CountAsync(p => p.CurrentWarehouseLocationId != null));

        var billing = await h.Billing.ComputeAsync(
            h.CustomerId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), CancellationToken.None);
        Assert.Equal(3, billing.OpenStays);
    }

    [Fact]
    public async Task Billing_IgnoresStaysEntirelyOutsideThePeriod()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.StorageStays.Add(new StorageStay
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = h.PackageId, WarehouseId = h.WarehouseId,
            InAt = new DateTime(2026, 7, 1), OutAt = new DateTime(2026, 7, 5),
        });
        await h.Db.Context.SaveChangesAsync();

        var billing = await h.Billing.ComputeAsync(
            h.CustomerId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), CancellationToken.None);

        Assert.Equal(0m, billing.TotalPalletDays);
        Assert.Empty(billing.PerOrder);
    }
}
