using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Profitability.Dtos;
using TransportationService.Api.Modules.Profitability.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Profitability;

/// <summary>
/// Profitability read models: revenue-source precedence (invoiced over agreed, paid kept
/// separate), actual-vs-estimated cost split with explicit missing-data indicators,
/// customer grouping with allocation marking, and tenant isolation.
/// </summary>
public class ProfitabilityQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 07, 18);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid TripId, Guid OrderId, Guid CustomerId)
    {
        public ProfitabilityQueryService Sut(Guid? tenantOverride = null) => new(
            Db.Context, new DevTenantContext(tenantOverride ?? TenantId), new TestClock(Now));
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = TripDate, Status = TransportOrderStatus.Completed, GoodsDescription = "Paletten",
            AgreedPrice = 1000m,
        });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = TripDate,
            Status = TripStatus.Completed, ActualDistanceKm = 200m,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        db.Context.TripCostSummaries.Add(new TripCostSummary
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId,
            EstimatedTotal = 700m, ActualTotal = 300m, ProjectedTotal = 800m, Revenue = 1000m,
        });
        db.Context.TripCostLines.AddRange(
            new TripCostLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId,
                Phase = TripCostPhase.Actual, CostType = TripCostType.Fuel,
                Description = "Diesel (tankbeurten)", Quantity = 80, Unit = "l", UnitRate = 1.8m, Amount = 144m,
                Source = TripCostLine.SourceFuelRecords, CalculatedAt = Now.UtcDateTime,
            },
            new TripCostLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId,
                Phase = TripCostPhase.Estimated, CostType = TripCostType.DriverLabour,
                Description = "Chauffeur (raming)", Quantity = 8, Unit = "u", UnitRate = 45m, Amount = 360m,
                Source = TripCostLine.SourceCalculated, CalculatedAt = Now.UtcDateTime,
            });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, tripId, orderId, customerId);
    }

    [Fact]
    public async Task Overview_UsesAgreedRevenue_UntilInvoiceLinesExist()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var before = await h.Sut().GetOverviewAsync(TripDate, TripDate, null, CancellationToken.None);
        var trip = Assert.Single(before.Trips);
        Assert.Equal(1000m, trip.AgreedRevenue);
        Assert.Equal(0m, trip.InvoicedRevenue);
        Assert.Equal(RevenueSource.Agreed, trip.RevenueSourceUsed);
        Assert.Equal(1000m, trip.RevenueUsed);

        // Invoice the order: invoiced revenue takes precedence; paid stays separate until paid.
        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.CustomerId, InvoiceNumber = "F-0001",
            InvoiceDate = TripDate.AddDays(2), DueDate = TripDate.AddDays(32), Status = InvoiceStatus.Sent,
        });
        h.Db.Context.InvoiceLines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = h.OrderId,
            Sequence = 1, Description = "Transport", Quantity = 1, UnitPrice = 1100m, VatRatePercent = 21,
        });
        await h.Db.Context.SaveChangesAsync();

        var after = await h.Sut().GetOverviewAsync(TripDate, TripDate, null, CancellationToken.None);
        var invoiced = Assert.Single(after.Trips);
        Assert.Equal(1100m, invoiced.InvoicedRevenue);
        Assert.Equal(RevenueSource.Invoiced, invoiced.RevenueSourceUsed);
        Assert.Equal(1100m, invoiced.RevenueUsed);
        Assert.Equal(0m, invoiced.PaidRevenue);
        Assert.Equal(1100m - 800m, invoiced.Margin);
    }

    [Fact]
    public async Task Overview_SplitsActualVsEstimated_AndFlagsMissingCoreTypes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var overview = await h.Sut().GetOverviewAsync(TripDate, TripDate, null, CancellationToken.None);
        var trip = Assert.Single(overview.Trips);

        Assert.Equal(300m, trip.ActualCost);
        Assert.Equal(700m, trip.EstimatedCost);
        Assert.Equal(800m, trip.ProjectedCost);
        // Fuel + DriverLabour have lines; VehicleDistance and Toll have none → explicitly missing.
        Assert.Contains(TripCostType.VehicleDistance, trip.MissingCostTypes);
        Assert.Contains(TripCostType.Toll, trip.MissingCostTypes);
        Assert.DoesNotContain(TripCostType.Fuel, trip.MissingCostTypes);
        Assert.Equal(1, overview.Summary.TripsWithMissingData);
        // km-metrics ride the actual distance.
        Assert.Equal(4m, trip.CostPerKm); // 800 / 200
        Assert.True(trip.DistanceIsActual);
    }

    [Fact]
    public async Task Explanation_ListsRevenueAndCostLines_WithSources()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var explanation = await h.Sut().GetExplanationAsync(h.TripId, CancellationToken.None);

        Assert.NotNull(explanation);
        Assert.Contains(explanation!.RevenueLines, l => l.Source == "Opdracht" && l.Amount == 1000m);
        Assert.Contains(explanation.CostLines, l => l.Source == TripCostLine.SourceFuelRecords && l.Phase == "Actual");
        Assert.Contains(explanation.CostLines, l => l.Phase == "Estimated");
        Assert.Contains(TripCostType.Toll, explanation.MissingCostTypes);
    }

    [Fact]
    public async Task GroupedByCustomer_And_TenantIsolation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var groups = await h.Sut().GetGroupedAsync(
            ProfitabilityDimension.Customer, TripDate, TripDate, CancellationToken.None);
        var group = Assert.Single(groups);
        Assert.Equal("Haven BV", group.Label);
        Assert.Equal(1000m, group.Revenue);
        Assert.Equal(800m, group.ProjectedCost);
        Assert.False(group.ContainsAllocatedCosts); // single-customer trip: booked, not allocated

        // Foreign tenant sees nothing.
        var foreign = await h.Sut(Guid.NewGuid()).GetOverviewAsync(TripDate, TripDate, null, CancellationToken.None);
        Assert.Empty(foreign.Trips);
    }
}
