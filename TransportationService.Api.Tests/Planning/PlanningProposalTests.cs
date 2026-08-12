using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Planning;

/// <summary>
/// Wave 7: the transparent tour-proposal heuristic. Ready = Confirmed + unplanned + date in
/// window; grouped per DELIVERY zone (the pricing-zone concept reused); overdue orders first;
/// unzoned and stop-less orders surface with their reason instead of disappearing.
/// </summary>
public class PlanningProposalTests
{
    private sealed record Harness(SqliteTestDbContext Db, PlanningProposalService Sut, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV", IsActive = true });
        var zone = new PricingZone { Id = Guid.NewGuid(), TenantId = tenantId, Code = "ANT", Name = "Antwerpen", IsActive = true };
        zone.Areas.Add(new PricingZoneArea
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ZoneId = zone.Id,
            CountryCode = "BE", PostalCodeFrom = "2000", PostalCodeTo = "2999",
        });
        db.Context.PricingZones.Add(zone);
        await db.Context.SaveChangesAsync();
        return new Harness(db, new PlanningProposalService(db.Context, new DevTenantContext(tenantId)), tenantId, customerId);
    }

    private static TransportOrder Order(
        Harness h, string number, DateOnly date, string? postal, TransportOrderStatus status = TransportOrderStatus.Confirmed,
        decimal? weight = null)
    {
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            OrderNumber = number, OrderDate = date, Status = status, WeightKg = weight,
        };
        if (postal is not null)
        {
            order.Stops.Add(new TransportOrderStop
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1,
                StopType = StopType.Unloading, PostalCode = postal, City = "Stad", CountryCode = "BE",
            });
        }

        h.Db.Context.TransportOrders.Add(order);
        return order;
    }

    [Fact]
    public async Task Proposals_GroupPerZone_OverdueFirst_UnzonedAndStopLessExplained()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = new DateOnly(2026, 8, 12);
        Order(h, "ORD-1", today, "2000", weight: 400);
        Order(h, "ORD-2", today.AddDays(-1), "2500", weight: 600);   // overdue → first in zone
        Order(h, "ORD-3", today, "9000");                            // no zone → Ongezoneerd
        Order(h, "ORD-4", today, null);                              // no stop → excluded with reason
        Order(h, "ORD-5", today, "2100", TransportOrderStatus.Draft); // not confirmed → invisible
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetProposalsAsync(today, CancellationToken.None);

        var ant = Assert.Single(result.Proposals, p => p.ZoneCode == "ANT");
        Assert.Equal(["ORD-2", "ORD-1"], ant.Orders.Select(o => o.OrderNumber).ToArray());
        Assert.True(ant.Orders[0].Overdue);
        Assert.Equal(1000m, ant.TotalWeightKg);
        Assert.Contains(ant.Explanations, e => e.Contains("achterstand"));

        var unzoned = Assert.Single(result.Proposals, p => p.ZoneName == "Ongezoneerd");
        Assert.Single(unzoned.Orders, o => o.OrderNumber == "ORD-3");
        Assert.Contains(unzoned.Explanations, e => e.Contains("Geen zone"));

        Assert.Contains(result.Excluded, e => e.Contains("ORD-4") && e.Contains("losstop"));
        Assert.DoesNotContain(result.Proposals.SelectMany(p => p.Orders), o => o.OrderNumber == "ORD-5");
    }

    [Fact]
    public async Task Proposals_SkipOrdersAlreadyOnAnActiveTrip_ButNotCancelledOnes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = new DateOnly(2026, 8, 12);
        var planned = Order(h, "ORD-1", today, "2000");
        var released = Order(h, "ORD-2", today, "2100");
        var activeTrip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1",
            TripDate = today, Status = TripStatus.Planned,
        };
        var cancelledTrip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-2",
            TripDate = today, Status = TripStatus.Cancelled,
        };
        h.Db.Context.Trips.AddRange(activeTrip, cancelledTrip);
        h.Db.Context.TripOrders.AddRange(
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = activeTrip.Id, TransportOrderId = planned.Id, Sequence = 1 },
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = cancelledTrip.Id, TransportOrderId = released.Id, Sequence = 1 });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetProposalsAsync(today, CancellationToken.None);

        var orders = result.Proposals.SelectMany(p => p.Orders).Select(o => o.OrderNumber).ToList();
        Assert.DoesNotContain("ORD-1", orders);
        Assert.Contains("ORD-2", orders);
    }

    [Fact]
    public async Task Proposals_ExplainConstraints_AdrEquipmentAndCapacity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = new DateOnly(2026, 8, 12);
        var adr = Order(h, "ORD-1", today, "2000", weight: 9000);
        adr.AdrRequired = true;
        adr.MoffettRequired = true;
        var plain = Order(h, "ORD-2", today, "2100", weight: 8000);
        // The tenant's largest vehicle carries 10 t — this 17 t tour cannot be one trip.
        h.Db.Context.Vehicles.Add(new TransportationService.Api.Modules.Fleet.Entities.Vehicle
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InternalNumber = "VRT-1", LicensePlate = "1-ABC-123",
            PayloadKg = 10000, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetProposalsAsync(today, CancellationToken.None);

        var ant = Assert.Single(result.Proposals, p => p.ZoneCode == "ANT");
        var adrOrder = Assert.Single(ant.Orders, o => o.OrderNumber == "ORD-1");
        Assert.Contains(adrOrder.Constraints, c => c.Contains("ADR"));
        Assert.Contains(adrOrder.Constraints, c => c.Contains("Moffett"));
        Assert.Empty(Assert.Single(ant.Orders, o => o.OrderNumber == "ORD-2").Constraints);
        // Infeasibility is explained, never hidden.
        Assert.Contains(ant.Explanations, e => e.Contains("overschrijdt het grootste voertuig"));
        Assert.Contains(ant.Explanations, e => e.Contains("voorwaarden"));
    }
}
