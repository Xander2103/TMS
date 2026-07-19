using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Eta.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Eta;

/// <summary>
/// Wave 10: the internal ETA foundation — sequential heuristic (deliberately not route
/// optimisation), manual delay, sticky dispatcher overrides, explainable statuses, history
/// and the customer-notification threshold. A provider seam replaces the heuristic later.
/// </summary>
public class EtaServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeRouteProvider : IRouteEstimationProvider
    {
        public int? MinutesBetweenStops { get; set; }

        public Task<IReadOnlyList<int>?> EstimateTravelMinutesAsync(
            RouteEstimationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<int>?>(
                MinutesBetweenStops is { } minutes
                    ? Enumerable.Repeat(minutes, request.Stops.Count).ToList()
                    : null);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, EtaService Sut, FakeRouteProvider Provider, TestClock Clock,
        Guid TenantId, Guid TripId, Guid LoadStopId, Guid UnloadStopId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var loadStopId = Guid.NewGuid();
        var unloadStopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DefaultLoadingMinutes = 20, DefaultUnloadingMinutes = 15,
        });

        // A dispatcher with planning.edit receives the late-crossing alerts.
        var dispatcherUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        db.Context.Users.Add(new TransportationService.Api.Modules.Identity.Entities.User
        {
            Id = dispatcherUserId, TenantId = tenantId, Email = "dispatch@acme.be", PasswordHash = "x",
            FirstName = "Dora", LastName = "Dispatch", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Roles.Add(new TransportationService.Api.Modules.Identity.Entities.Role
        {
            Id = roleId, TenantId = tenantId, Name = "Dispatch", IsActive = true,
        });
        db.Context.Permissions.Add(new TransportationService.Api.Modules.Identity.Entities.Permission
        {
            Id = permissionId, Code = "planning.edit", Module = "planning", Action = "edit",
        });
        db.Context.RolePermissions.Add(new TransportationService.Api.Modules.Identity.Entities.RolePermission
        {
            RoleId = roleId, PermissionId = permissionId,
        });
        db.Context.UserRoles.Add(new TransportationService.Api.Modules.Identity.Entities.UserRole
        {
            UserId = dispatcherUserId, RoleId = roleId,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            Email = "planning@haven.be", IsActive = true,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop
            {
                Id = loadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1,
                StopType = StopType.Loading, City = "Antwerpen",
            },
            new TransportOrderStop
            {
                Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2,
                StopType = StopType.Unloading, City = "Gent",
                // Bound one hour from now: the heuristic ETA (30+20+30) lands after it -> Late.
                ConfirmedTo = Now.UtcDateTime.AddHours(1),
            });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var provider = new FakeRouteProvider();
        var sut = new EtaService(
            db.Context, tenant, user, provider,
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, clock),
            new MessageOutboxService(db.Context, tenant, clock),
            clock);
        return new Harness(db, sut, provider, clock, tenantId, tripId, loadStopId, unloadStopId);
    }

    [Fact]
    public async Task Recalculate_SequencesPendingStops_WithHandlingTimes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        Assert.Equal(2, result!.Stops.Count);
        var load = result.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        var unload = result.Stops.Single(s => s.TransportOrderStopId == h.UnloadStopId);
        // Heuristic: 30 min travel to stop 1; then 20 min loading + 30 min travel to stop 2.
        Assert.Equal(Now.UtcDateTime.AddMinutes(30), load.CurrentEta);
        Assert.Equal(Now.UtcDateTime.AddMinutes(80), unload.CurrentEta);
        Assert.Equal(EtaSource.Heuristic, load.Source);
        // No bound on the load stop -> OnTime; the unload bound (60 min) is beaten by 80 min -> Late.
        Assert.Equal(EtaStatus.OnTime, load.Status);
        Assert.Equal(EtaStatus.Late, unload.Status);
    }

    [Fact]
    public async Task Provider_WhenAvailable_ReplacesHeuristicTravel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Provider.MinutesBetweenStops = 10;

        var result = await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        var load = result!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        Assert.Equal(Now.UtcDateTime.AddMinutes(10), load.CurrentEta);
        Assert.Equal(EtaSource.Provider, load.Source);
    }

    [Fact]
    public async Task ManualDelay_ShiftsEtas_AndRecordsHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        var delayed = await h.Sut.SetTripDelayAsync(h.TripId, 45, "File op de E17", CancellationToken.None);

        var load = delayed!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        Assert.Equal(Now.UtcDateTime.AddMinutes(75), load.CurrentEta);
        Assert.Equal(45, delayed.ManualDelayMinutes);

        // Two history entries per stop: initial + delayed.
        var history = await h.Sut.GetStopEtaHistoryAsync(h.TripId, h.LoadStopId, CancellationToken.None);
        Assert.Equal(2, history!.Count);
    }

    [Fact]
    public async Task Override_Sticks_ThroughRecalc_UntilCleared()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        var overrideEta = Now.UtcDateTime.AddHours(3);
        var overridden = await h.Sut.OverrideStopEtaAsync(h.TripId, h.LoadStopId, overrideEta, "Klant vraagt latere levering", CancellationToken.None);
        Assert.Equal(EtaSource.DispatcherOverride,
            overridden!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId).Source);

        // A recalculation leaves the override alone.
        var recalculated = await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);
        var load = recalculated!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        Assert.Equal(overrideEta, load.CurrentEta);
        Assert.Equal(EtaSource.DispatcherOverride, load.Source);

        // Clearing restores the heuristic on the next pass.
        var cleared = await h.Sut.ClearStopEtaOverrideAsync(h.TripId, h.LoadStopId, CancellationToken.None);
        Assert.Equal(EtaSource.Heuristic,
            cleared!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId).Source);
    }

    [Fact]
    public async Task History_OnlyRecordsRealChanges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        var history = await h.Sut.GetStopEtaHistoryAsync(h.TripId, h.LoadStopId, CancellationToken.None);

        Assert.Single(history!);
    }

    [Fact]
    public async Task TurningLate_NotifiesDispatch_AndQueuesCustomerMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        // The unload stop became Late immediately on first calculation.
        Assert.Contains(h.Db.Context.Notifications, n => n.Type == "eta_changed");
        Assert.Contains(h.Db.Context.OutboxMessages, m => m.Kind == "eta_update");

        // Recalculating without change does not spam.
        var notificationCount = h.Db.Context.Notifications.Count();
        var outboxCount = h.Db.Context.OutboxMessages.Count();
        await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);
        Assert.Equal(notificationCount, h.Db.Context.Notifications.Count());
        Assert.Equal(outboxCount, h.Db.Context.OutboxMessages.Count());
    }

    [Fact]
    public async Task TerminalStops_AreSkipped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            TransportOrderStopId = h.LoadStopId, Status = StopExecutionStatus.Completed,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RecalculateTripAsync(h.TripId, CancellationToken.None);

        Assert.DoesNotContain(result!.Stops, s => s.TransportOrderStopId == h.LoadStopId);
        var unload = result.Stops.Single(s => s.TransportOrderStopId == h.UnloadStopId);
        // Only travel to the single remaining stop: 30 minutes.
        Assert.Equal(Now.UtcDateTime.AddMinutes(30), unload.CurrentEta);
    }
}
