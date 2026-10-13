using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Seeding;

public class DevDemoDataSeederTests
{
    private static async Task<Guid> SeedDevTenantAsync(SqliteTestDbContext db)
    {
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Dev", Slug = "dev", CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return tenantId;
    }

    [Fact]
    public async Task SeedAsync_CreatesDemoMasterData()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedDevTenantAsync(db);

        await DevDemoDataSeeder.SeedAsync(db.Context, NullLogger.Instance);

        Assert.Equal(5, await db.Context.Customers.CountAsync(c => c.TenantId == tenantId));
        Assert.Equal(10, await db.Context.Employees.CountAsync(e => e.TenantId == tenantId));

        // Every customer has 2-4 contacts and 3-5 locations, at least one location inactive overall.
        var contacts = await db.Context.CustomerContacts.Where(c => c.TenantId == tenantId).ToListAsync();
        Assert.InRange(contacts.Count, 10, 20);
        Assert.Contains(contacts, c => c.ContactType == CustomerContactType.Planning && c.IsPrimary);
        var locations = await db.Context.Locations.Where(l => l.TenantId == tenantId).ToListAsync();
        Assert.InRange(locations.Count, 15, 25);
        Assert.Contains(locations, l => !l.IsActive);
        Assert.Contains(locations, l => l.Type == LocationType.ConstructionSite);

        // Operational warehouse locations carry opening hours + access data.
        var warehouse = locations.First(l => l.Type == LocationType.Warehouse);
        Assert.Equal("Poort 2", warehouse.Gate);
        Assert.True(await db.Context.Set<LocationOpeningInterval>()
            .AnyAsync(i => i.LocationId == warehouse.Id && i.DayOfWeek == 1));

        // Drivers + notes exist; at least one pinned note.
        Assert.Equal(4, await db.Context.Set<Modules.Drivers.Entities.Driver>().CountAsync(d => d.TenantId == tenantId));
        Assert.True(await db.Context.Set<Modules.Employees.Entities.EmployeeNote>()
            .AnyAsync(n => n.TenantId == tenantId && n.IsPinnedToDashboard));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedDevTenantAsync(db);

        await DevDemoDataSeeder.SeedAsync(db.Context, NullLogger.Instance);
        await DevDemoDataSeeder.SeedAsync(db.Context, NullLogger.Instance);

        Assert.Equal(5, await db.Context.Customers.CountAsync(c => c.TenantId == tenantId));
        Assert.Equal(10, await db.Context.Employees.CountAsync(e => e.TenantId == tenantId));
    }

    [Fact]
    public async Task SeedAsync_WithoutDevTenant_DoesNothing()
    {
        using var db = new SqliteTestDbContext();

        await DevDemoDataSeeder.SeedAsync(db.Context, NullLogger.Instance);

        Assert.Empty(await db.Context.Customers.ToListAsync());
    }
}
