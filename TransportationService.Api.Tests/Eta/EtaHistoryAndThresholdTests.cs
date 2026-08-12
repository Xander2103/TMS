using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Eta.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Eta;

/// <summary>
/// Wave 8: historical stop-duration estimates (measured location times beat the tenant
/// default, ≥3 samples) and the ETA-shift threshold (a configured tenant threshold messages
/// the customer when the ETA moves ≥ X minutes, also while still on time).
/// </summary>
public class EtaHistoryAndThresholdTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 8, 0, 0, TimeSpan.Zero);

    private sealed class NoProvider : IRouteEstimationProvider
    {
        public Task<IReadOnlyList<int>?> EstimateTravelMinutesAsync(
            RouteEstimationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<int>?>(null);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, EtaService Sut, TestClock Clock,
        Guid TenantId, Guid TripId, Guid Stop1Id, Guid Stop2Id, Guid LocationId);

    private static async Task<Harness> SeedAsync(int? shiftNotifyMinutes = null)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stop1Id = Guid.NewGuid();
        var stop2Id = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DefaultLoadingMinutes = 30, DefaultUnloadingMinutes = 30,
            EtaShiftNotifyMinutes = shiftNotifyMinutes,
        });
        db.Context.Locations.Add(new Location { Id = locationId, TenantId = tenantId, Name = "Kade 5", City = "Antwerpen", IsActive = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            Email = "planning@haven.be", IsActive = true,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 8, 11), Status = TransportOrderStatus.InProgress,
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop
            {
                Id = stop1Id, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1,
                StopType = StopType.Loading, City = "Antwerpen", LocationId = locationId,
            },
            new TransportOrderStop
            {
                Id = stop2Id, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2,
                StopType = StopType.Unloading, City = "Gent",
            });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 8, 12),
            Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var sut = new EtaService(
            db.Context, tenant, user, new NoProvider(),
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, clock),
            new MessageOutboxService(db.Context, tenant, clock),
            clock);
        return new Harness(db, sut, clock, tenantId, tripId, stop1Id, stop2Id, locationId);
    }

    /// <summary>Seeds N historical executions of the given duration at the harness location.</summary>
    private static async Task SeedHistoryAsync(Harness h, int samples, int minutesEach)
    {
        for (var i = 0; i < samples; i += 1)
        {
            var histOrderId = Guid.NewGuid();
            var histStopId = Guid.NewGuid();
            var histTripId = Guid.NewGuid();
            h.Db.Context.TransportOrders.Add(new TransportOrder
            {
                Id = histOrderId, TenantId = h.TenantId, OrderNumber = $"HIST-{i}",
                OrderDate = new(2026, 8, 1),
                CustomerId = await h.Db.Context.Customers.Select(c => c.Id).FirstAsync(),
            });
            h.Db.Context.TransportOrderStops.Add(new TransportOrderStop
            {
                Id = histStopId, TenantId = h.TenantId, TransportOrderId = histOrderId, Sequence = 1,
                StopType = StopType.Loading, City = "Antwerpen", LocationId = h.LocationId,
            });
            h.Db.Context.Trips.Add(new Trip
            {
                Id = histTripId, TenantId = h.TenantId, TripNumber = $"HIST-RIT-{i}",
                TripDate = new(2026, 8, 1), Status = TripStatus.Completed,
            });
            h.Db.Context.StopExecutions.Add(new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = histTripId, TransportOrderStopId = histStopId,
                Status = StopExecutionStatus.Completed,
                ArrivedAt = Now.UtcDateTime.AddDays(-5),
                DepartedAt = Now.UtcDateTime.AddDays(-5).AddMinutes(minutesEach),
            });
        }

        await h.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task HistoricalDurations_BeatTheTenantDefault_WithEnoughSamples()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedHistoryAsync(h, samples: 3, minutesEach: 90);

        var result = await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        // Stop2 follows stop1 by measured handling (90) + heuristic travel (30) = 120 minutes;
        // the tenant default (30) would have given 60.
        var stop1 = result!.Stops.Single(s => s.TransportOrderStopId == h.Stop1Id);
        var stop2 = result.Stops.Single(s => s.TransportOrderStopId == h.Stop2Id);
        Assert.Equal(120, (stop2.CurrentEta - stop1.CurrentEta).TotalMinutes);
    }

    [Fact]
    public async Task HistoricalDurations_IgnoreTooFewSamples()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedHistoryAsync(h, samples: 2, minutesEach: 90);

        var result = await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        var stop1 = result!.Stops.Single(s => s.TransportOrderStopId == h.Stop1Id);
        var stop2 = result.Stops.Single(s => s.TransportOrderStopId == h.Stop2Id);
        Assert.Equal(60, (stop2.CurrentEta - stop1.CurrentEta).TotalMinutes); // default 30 + travel 30
    }

    [Fact]
    public async Task ShiftThreshold_MessagesTheCustomer_WhenTheEtaMovesEnough()
    {
        var h = await SeedAsync(shiftNotifyMinutes: 15);
        using var _ = h.Db;
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);
        Assert.DoesNotContain(h.Db.Context.OutboxMessages, m => m.Kind == "eta_update"); // first calc = no shift

        // A 30-minute dispatcher delay moves every pending ETA ≥ the 15-minute threshold.
        await h.Sut.SetTripDelayAsync(h.TripId, 30, "File op de ring", CancellationToken.None);

        Assert.Contains(h.Db.Context.OutboxMessages, m => m.Kind == "eta_update");
    }

    [Fact]
    public async Task ShiftThreshold_Off_KeepsTheOldBehaviour()
    {
        var h = await SeedAsync(shiftNotifyMinutes: null);
        using var _ = h.Db;
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);
        await h.Sut.SetTripDelayAsync(h.TripId, 30, "File", CancellationToken.None);

        Assert.DoesNotContain(h.Db.Context.OutboxMessages, m => m.Kind == "eta_update");
    }
}
