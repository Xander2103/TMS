using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

public class CoverageStatusBackfillTests
{
    [Theory]
    [InlineData("""[{"status":"Full"},{"status":"Full"}]""", "Full")]
    [InlineData("""[{"status":"Full"},{"status":"Partial"}]""", "Partial")]
    [InlineData("""[{"status":"Partial"},{"status":"None"}]""", "None")]
    [InlineData("[]", "NotApplicable")]
    [InlineData("not json at all", "NotApplicable")]
    public void Derive_WorstEntryWins(string json, string expected) =>
        Assert.Equal(expected, CoverageStatusBackfillSeeder.Derive(json));

    [Fact]
    public async Task Backfill_FillsOnlyMissingStatuses_AndIsIdempotent()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant" });
        var secondOrderId = Guid.NewGuid();
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new DateOnly(2026, 8, 1),
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = secondOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-2",
            OrderDate = new DateOnly(2026, 8, 2),
        });
        var legacy = new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
            TariffDate = new DateOnly(2026, 8, 1), Currency = "EUR",
            CoverageJson = """[{"status":"Full"},{"status":"None"}]""",
        };
        var alreadyTyped = new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = secondOrderId,
            TariffDate = new DateOnly(2026, 8, 2), Currency = "EUR",
            CoverageJson = """[{"status":"Full"}]""", CoverageStatus = "Partial", // pre-set stays untouched
        };
        db.Context.TransportOrderPricingSnapshots.AddRange(legacy, alreadyTyped);
        await db.Context.SaveChangesAsync();

        var first = await CoverageStatusBackfillSeeder.SyncAsync(db.Context);
        var second = await CoverageStatusBackfillSeeder.SyncAsync(db.Context);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal("None", (await db.Context.TransportOrderPricingSnapshots.SingleAsync(s => s.Id == legacy.Id)).CoverageStatus);
        Assert.Equal("Partial", (await db.Context.TransportOrderPricingSnapshots.SingleAsync(s => s.Id == alreadyTyped.Id)).CoverageStatus);
        // The frozen JSON itself is never rewritten.
        Assert.Equal("""[{"status":"Full"},{"status":"None"}]""", (await db.Context.TransportOrderPricingSnapshots.SingleAsync(s => s.Id == legacy.Id)).CoverageJson);
    }
}
