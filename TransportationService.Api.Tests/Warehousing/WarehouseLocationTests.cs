using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Dtos;
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Modules.Warehousing.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Warehousing;

/// <summary>
/// Wave 4 §1: warehouse storage locations (warehouse → zone → position). Two levels max,
/// per-warehouse unique codes, and deletion is blocked while positions or projected packages
/// still reference the location.
/// </summary>
public class WarehouseLocationTests
{
    private sealed record Harness(SqliteTestDbContext Db, WarehouseAdminService Sut, Guid TenantId, Guid WarehouseId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Locations.Add(new Location { Id = locationId, TenantId = tenantId, Name = "Depot", City = "Antwerpen", IsActive = true });
        db.Context.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = tenantId, Name = "Magazijn A", LocationId = locationId, IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new WarehouseAdminService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, sut, tenantId, warehouseId);
    }

    [Fact]
    public async Task SaveAndList_RoundTripsAZoneWithPositions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var zone = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A", "Zone A"), CancellationToken.None);
        var position = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A-01", "Positie A-01", ParentId: zone!.Id), CancellationToken.None);

        Assert.Equal("Zone", zone.Kind);
        Assert.Equal("Position", position!.Kind); // parented → always a position
        var listed = await h.Sut.ListLocationsAsync(h.WarehouseId, CancellationToken.None);
        Assert.Equal(2, listed!.Count);
        Assert.Contains(listed, l => l.Code == "A-01" && l.ParentId == zone.Id);
    }

    [Fact]
    public async Task Validation_TwoLevelsMax_UniqueCodes_ParentInSameWarehouse()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zone = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A", "Zone A"), CancellationToken.None);
        var position = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A-01", "Positie", ParentId: zone!.Id), CancellationToken.None);

        // A position under a position is refused.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A-01-X", "Te diep", ParentId: position!.Id), CancellationToken.None));

        // Duplicate code (case-insensitive) is refused.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("a", "Dubbel"), CancellationToken.None));

        // Unknown parent is refused.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("B-01", "Zwevend", ParentId: Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_BlocksWhilePositionsOrProjectedPackagesRemain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zone = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A", "Zone A"), CancellationToken.None);
        var position = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A-01", "Positie", ParentId: zone!.Id), CancellationToken.None);

        // Zone with a position → blocked.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.DeleteLocationAsync(h.WarehouseId, zone.Id, CancellationToken.None));

        // Position with a projected package → blocked.
        var customerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Modules.Partners.Entities.Customer
        {
            Id = customerId, TenantId = h.TenantId, CustomerNumber = "KL-1", Name = "Klant", IsActive = true,
        });
        var orderId = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new Modules.Orders.Entities.TransportOrder
        {
            Id = orderId, TenantId = h.TenantId, OrderNumber = "ORD-1", OrderDate = new DateOnly(2026, 8, 12),
            CustomerId = customerId,
        });
        h.Db.Context.Packages.Add(new Package
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId,
            PackageNumber = "PKG-1", CurrentWarehouseLocationId = position!.Id,
        });
        await h.Db.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.DeleteLocationAsync(h.WarehouseId, position.Id, CancellationToken.None));

        // Empty position deletes fine.
        var empty = await h.Sut.SaveLocationAsync(h.WarehouseId, null,
            new SaveWarehouseLocationRequest("A-02", "Leeg", ParentId: zone.Id), CancellationToken.None);
        Assert.True(await h.Sut.DeleteLocationAsync(h.WarehouseId, empty!.Id, CancellationToken.None));
    }
}
