using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>
/// P7: commercial services quantified from ACTUAL warehouse events — handling-in from receive
/// scans, picking from staging scans, storage from the movement clock — with entered
/// quantities still winning and recalculation idempotent (LineKey merge, distinct packages).
/// </summary>
public class EventDerivedChargeTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 14, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now),
            new PricingEngine(db.Context, tenant), user, new PermissionSet());
        return new Harness(db, orders, new PricingAdminService(db.Context, tenant, audit), tenantId, customerId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Paletten", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")]);

    private static UpdateTransportOrderRequest UpdateRequest(Guid customerId) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Paletten", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")]);

    private static void AddPackageWithEvents(
        Harness h, Guid orderId, string number, params PackageEventType[] events)
    {
        var packageId = Guid.NewGuid();
        h.Db.Context.Packages.Add(new Package
        {
            Id = packageId, TenantId = h.TenantId, TransportOrderId = orderId,
            PackageNumber = number, BarcodeValue = $"{number}-BC",
        });
        foreach (var (eventType, index) in events.Select((e, i) => (e, i)))
        {
            h.Db.Context.PackageEvents.Add(new PackageEvent
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = packageId,
                EventType = eventType, OccurredAt = Now.UtcDateTime.AddMinutes(index),
            });
        }
    }

    [Fact]
    public async Task HandlingInAndPicking_CountActualScans_NotOrderedQuantities()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "HANDIN", "Handling IN", SurchargeKind.PerUnit, 2.00m, true, 1,
            AutoApply: true, QuantitySource: "ScannedIn"), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 2,
            AutoApply: true, QuantitySource: "Picked"), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var orderId = created.Order!.Id;

        // 5 packages scanned IN; 3 of them staged (picked). Duplicate events on the same
        // package must not inflate the count (distinct packages).
        for (var i = 1; i <= 5; i++)
        {
            var events = i <= 3
                ? new[] { PackageEventType.Received, PackageEventType.Received, PackageEventType.Staged }
                : new[] { PackageEventType.Received };
            AddPackageWithEvents(h, orderId, $"PKG-{i}", events);
        }
        await h.Db.Context.SaveChangesAsync();

        var updated = await h.Orders.UpdateAsync(orderId, UpdateRequest(h.CustomerId), CancellationToken.None);

        // Handling IN: 5 × €2 = €10; Picking: 3 × €1,25 = €3,75.
        Assert.Equal(13.75m, updated.Order!.AgreedPrice);
        var serviceLines = h.Db.Context.TransportOrderServiceLines
            .Where(l => l.TransportOrderId == orderId).ToList();
        Assert.Equal(5m, serviceLines.Single(l => l.NameSnapshot.Contains("Handling IN")).Quantity);
        Assert.Equal(3m, serviceLines.Single(l => l.NameSnapshot.Contains("Picking")).Quantity);

        // Reprocessing (recalc) is idempotent: same two lines, no duplicates, same total.
        var again = await h.Orders.UpdateAsync(orderId, UpdateRequest(h.CustomerId), CancellationToken.None);
        Assert.Equal(13.75m, again.Order!.AgreedPrice);
        Assert.Equal(2, h.Db.Context.TransportOrderServiceLines.Count(l => l.TransportOrderId == orderId));
    }

    [Fact]
    public async Task PalletDaySource_DerivesFromTheStorageClock_PartialOutbound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "OPSLAG", "Palletopslag", SurchargeKind.PerPalletDay, 0.50m, true, 1,
            AutoApply: true, QuantitySource: "PalletDays"), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var orderId = created.Order!.Id;
        var warehouseLocationId = Guid.NewGuid();
        var masterLocationId = Guid.NewGuid();
        h.Db.Context.Locations.Add(new Modules.Locations.Entities.Location
        {
            Id = masterLocationId, TenantId = h.TenantId, Name = "Depot", City = "Gent", IsActive = true,
        });
        h.Db.Context.Warehouses.Add(new Modules.Warehousing.Entities.Warehouse
        {
            Id = warehouseLocationId, TenantId = h.TenantId, Name = "Magazijn A", LocationId = masterLocationId, IsActive = true,
        });
        // Two packages: one stayed 2 started days (closed), one open for 3 days to "now".
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        h.Db.Context.Packages.AddRange(
            new Package { Id = p1, TenantId = h.TenantId, TransportOrderId = orderId, PackageNumber = "PKG-1", BarcodeValue = "PKG-1-BC" },
            new Package { Id = p2, TenantId = h.TenantId, TransportOrderId = orderId, PackageNumber = "PKG-2", BarcodeValue = "PKG-2-BC" });
        h.Db.Context.StorageStays.AddRange(
            new Modules.Warehousing.Entities.StorageStay
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = p1, WarehouseId = warehouseLocationId,
                InAt = Now.UtcDateTime.AddDays(-10), OutAt = Now.UtcDateTime.AddDays(-8.5),
            },
            new Modules.Warehousing.Entities.StorageStay
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = p2, WarehouseId = warehouseLocationId,
                InAt = Now.UtcDateTime.AddDays(-3), OutAt = null,
            });
        await h.Db.Context.SaveChangesAsync();

        var updated = await h.Orders.UpdateAsync(orderId, UpdateRequest(h.CustomerId), CancellationToken.None);

        // 2 started days (closed) + 3 started days (open, to now) = 5 pallet-days × €0,50.
        Assert.Equal(2.50m, updated.Order!.AgreedPrice);
        var line = h.Db.Context.TransportOrderServiceLines.Single(l => l.TransportOrderId == orderId);
        Assert.Equal(5m, line.Quantity);
    }

    [Fact]
    public async Task WithoutScans_EventSourcedService_StaysInformational()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "HANDIN", "Handling IN", SurchargeKind.PerUnit, 2.00m, true, 1,
            AutoApply: true, QuantitySource: "ScannedIn"), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);

        // No scans yet: no charge line, no silent €0 — the order remains priceless but honest.
        Assert.Empty(h.Db.Context.TransportOrderServiceLines
            .Where(l => l.TransportOrderId == created.Order!.Id && l.Amount != 0).ToList());
    }
}
