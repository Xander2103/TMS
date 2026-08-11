using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

public class DossierBackfillTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync(int nextDossierNumber = 1)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DossierNumberPrefix = "DOS-", DossierNumberNextValue = nextDossierNumber,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, customerId);
    }

    private static TransportOrder Order(Harness h, string number, TransportOrderStatus status = TransportOrderStatus.Draft) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, OrderNumber = number,
        OrderDate = new DateOnly(2026, 7, 1), Status = status,
        CustomerReference = "REF-9", GoodsDescription = "2 paletten",
    };

    [Fact]
    public async Task Backfill_WrapsUnlinkedOrder_WithActivityLinkAndCopiedFields()
    {
        var h = await SeedAsync(nextDossierNumber: 5);
        using var _ = h.Db;
        var order = Order(h, "ORD-1", TransportOrderStatus.Completed);
        h.Db.Context.TransportOrders.Add(order);
        await h.Db.Context.SaveChangesAsync();

        var created = await DossierBackfillSeeder.SyncAsync(h.Db.Context);

        Assert.Equal(1, created);
        var dossier = await h.Db.Context.TransportDossiers
            .SingleAsync(d => d.OriginTransportOrderId == order.Id);
        Assert.Equal("DOS-0005", dossier.DossierNumber);
        Assert.Equal("ORD-1 — Klant BV", dossier.Title);
        Assert.Equal(h.CustomerId, dossier.CustomerId);
        Assert.Equal("REF-9", dossier.CustomerReference);
        Assert.Equal(new DateOnly(2026, 7, 1), dossier.DossierDate);
        Assert.Equal(DossierStatus.Closed, dossier.Status); // Completed order → closed wrapper

        var activity = await h.Db.Context.DossierActivities.SingleAsync(a => a.DossierId == dossier.Id);
        Assert.Equal(order.Id, activity.LinkedTransportOrderId);
        var type = await h.Db.Context.ActivityTypes.SingleAsync(t => t.Id == activity.ActivityTypeId);
        Assert.True(type.IsSystemDefaultTransport);
        Assert.Single(await h.Db.Context.DossierOrders
            .Where(l => l.DossierId == dossier.Id && l.TransportOrderId == order.Id).ToListAsync());

        // Counter advanced.
        var settings = await h.Db.Context.TenantSettings.SingleAsync(s => s.TenantId == h.TenantId);
        Assert.Equal(6, settings.DossierNumberNextValue);
    }

    [Fact]
    public async Task Backfill_IsIdempotent_AndOpenOrderStaysOpen()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TransportOrders.Add(Order(h, "ORD-1", TransportOrderStatus.Confirmed));
        await h.Db.Context.SaveChangesAsync();

        var first = await DossierBackfillSeeder.SyncAsync(h.Db.Context);
        var second = await DossierBackfillSeeder.SyncAsync(h.Db.Context);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var dossier = await h.Db.Context.TransportDossiers.SingleAsync(d => d.TenantId == h.TenantId);
        Assert.Equal(DossierStatus.Open, dossier.Status);
    }

    [Fact]
    public async Task Backfill_SkipsOrders_AlreadyInUserCreatedDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = Order(h, "ORD-1");
        h.Db.Context.TransportOrders.Add(order);
        var userDossier = new TransportDossier
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierNumber = "DOS-9999", Title = "Project X",
        };
        h.Db.Context.TransportDossiers.Add(userDossier);
        h.Db.Context.DossierOrders.Add(new DossierOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = userDossier.Id, TransportOrderId = order.Id,
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await DossierBackfillSeeder.SyncAsync(h.Db.Context);

        Assert.Equal(0, created);
        Assert.Single(await h.Db.Context.TransportDossiers.Where(d => d.TenantId == h.TenantId).ToListAsync());
        Assert.Empty(await h.Db.Context.DossierActivities.Where(a => a.TenantId == h.TenantId).ToListAsync());
    }

    [Fact]
    public async Task Backfill_ProcessesTenantsIndependently()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tenantB = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = tenantB, Name = "B", Slug = "b", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantB, DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
        });
        h.Db.Context.Customers.Add(new Customer { Id = customerB, TenantId = tenantB, CustomerNumber = "KL-1", Name = "Ander NV" });
        h.Db.Context.TransportOrders.Add(Order(h, "ORD-A"));
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantB, CustomerId = customerB, OrderNumber = "ORD-B",
            OrderDate = new DateOnly(2026, 7, 2), Status = TransportOrderStatus.Draft,
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await DossierBackfillSeeder.SyncAsync(h.Db.Context);

        Assert.Equal(2, created);
        Assert.Equal("DOS-0001", (await h.Db.Context.TransportDossiers.SingleAsync(d => d.TenantId == h.TenantId)).DossierNumber);
        Assert.Equal("DOS-0001", (await h.Db.Context.TransportDossiers.SingleAsync(d => d.TenantId == tenantB)).DossierNumber);
        Assert.Equal(10, await h.Db.Context.ActivityTypes.CountAsync(t => t.TenantId == tenantB)); // lazily seeded
    }

    [Fact]
    public async Task Backfill_FallsBackToActiveHasStopsType_WhenTenantReshapedCatalogue()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Pre-seed and reshape: drop the default flag entirely, keep one HasStops type active.
        await new ActivityTypeSeeder(h.Db.Context, new TransportationService.Api.Modules.Tenancy.Services.DevTenantContext(h.TenantId))
            .EnsureSeededAsync(CancellationToken.None);
        foreach (var type in await h.Db.Context.ActivityTypes.Where(t => t.TenantId == h.TenantId).ToListAsync())
        {
            type.IsSystemDefaultTransport = false;
            type.IsActive = type.Code == "EXPRESS";
        }

        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.TransportOrders.Add(Order(h, "ORD-1"));
        await h.Db.Context.SaveChangesAsync();

        var created = await DossierBackfillSeeder.SyncAsync(h.Db.Context);

        Assert.Equal(1, created);
        var activity = await h.Db.Context.DossierActivities.SingleAsync(a => a.TenantId == h.TenantId);
        var usedType = await h.Db.Context.ActivityTypes.SingleAsync(t => t.Id == activity.ActivityTypeId);
        Assert.Equal("EXPRESS", usedType.Code);
    }
}
