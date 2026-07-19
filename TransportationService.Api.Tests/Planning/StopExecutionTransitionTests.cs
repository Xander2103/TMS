using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Planning;

/// <summary>
/// Wave 1: the controlled stop-status machine (Planned..Skipped), mandatory reasons,
/// late-arrival enforcement, the implicit arrival bridge, status history and time windows.
/// </summary>
public class StopExecutionTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TripExecutionService Sut, TestClock Clock, Guid TenantId, Guid TripId,
        Guid OrderId, Guid LoadStopId, Guid UnloadStopId, Guid DriverUserId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var loadStopId = Guid.NewGuid();
        var unloadStopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "chauffeur@acme.be", PasswordHash = "x",
            FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Terminal Links", City = "Antwerpen",
            LoadingInstructions = "Aanmelden aan dok 5", AccessInstructions = "Alfapass verplicht", IsActive = true,
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
                StopType = StopType.Loading, LocationId = locationId,
                RequestedFrom = Now.UtcDateTime.AddHours(-2), RequestedTo = Now.UtcDateTime.AddHours(4),
                AppointmentRequired = true, AppointmentReference = "SLOT-77",
            },
            new TransportOrderStop
            {
                Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2,
                StopType = StopType.Unloading, City = "Gent",
            });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            DriverId = driverId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(userId));
        var planningSync = new TripPlanningSyncService(db.Context, tenant);
        var tripService = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(userId), clock),
            planningSync, CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        var sut = new TripExecutionService(db.Context, tenant, new DevCurrentUserContext(userId), audit, tripService, planningSync,
            TripPackageTestFactory.Create(db.Context, tenant, clock),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(db.Context, tenant, new DevCurrentUserContext(userId), clock), clock);
        return new Harness(db, sut, clock, tenantId, tripId, orderId, loadStopId, unloadStopId, userId, locationId);
    }

    private static Task<ExecutionResult> Go(
        Harness h, Guid stopId, StopExecutionStatus to, string? reason = null, string? notes = null) =>
        h.Sut.TransitionAsync(h.TripId, stopId, new TransitionStopRequest(to, reason, notes), true, CancellationToken.None);

    private static ExecutionStopDto StopOf(ExecutionResult result, Guid stopId) =>
        result.Execution!.Stops.Single(s => s.TransportOrderStopId == stopId);

    [Fact]
    public async Task HappyPath_LoadingStop_StampsActualsAndWritesHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Go(h, h.LoadStopId, StopExecutionStatus.EnRoute);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await Go(h, h.LoadStopId, StopExecutionStatus.Arrived);
        h.Clock.Advance(TimeSpan.FromMinutes(25));
        await Go(h, h.LoadStopId, StopExecutionStatus.Loading);
        h.Clock.Advance(TimeSpan.FromMinutes(20));
        await Go(h, h.LoadStopId, StopExecutionStatus.Loaded);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var final = await Go(h, h.LoadStopId, StopExecutionStatus.Completed);

        Assert.Equal(ExecutionOutcome.Success, final.Outcome);
        var stop = StopOf(final, h.LoadStopId);
        Assert.Equal(StopExecutionStatus.Completed, stop.Status);
        Assert.Equal(Now.UtcDateTime.AddMinutes(30), stop.ArrivedAt);
        Assert.Equal(Now.UtcDateTime.AddMinutes(80), stop.CompletedAt);
        Assert.Equal(Now.UtcDateTime.AddMinutes(80), stop.DepartedAt);
        // Waiting = arrival until handling started (25 minutes), not the full dwell.
        Assert.Equal(25, stop.WaitingMinutes);

        var history = await h.Sut.GetStopHistoryAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.Success, history.Outcome);
        Assert.Equal(
            new[]
            {
                (StopExecutionStatus.Planned, StopExecutionStatus.EnRoute),
                (StopExecutionStatus.EnRoute, StopExecutionStatus.Arrived),
                (StopExecutionStatus.Arrived, StopExecutionStatus.Loading),
                (StopExecutionStatus.Loading, StopExecutionStatus.Loaded),
                (StopExecutionStatus.Loaded, StopExecutionStatus.Completed),
            },
            history.History!.Select(x => (x.FromStatus, x.ToStatus)).ToArray());
        Assert.All(history.History!, x => Assert.Equal("Jan Jansen", x.UserName));
    }

    [Fact]
    public async Task Transition_FromTerminalStatus_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Go(h, h.LoadStopId, StopExecutionStatus.Completed);
        var result = await Go(h, h.LoadStopId, StopExecutionStatus.EnRoute);

        Assert.Equal(ExecutionOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Transition_HandlingState_MustMatchStopType()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var loadingOnUnload = await Go(h, h.UnloadStopId, StopExecutionStatus.Loading);
        var unloadingOnLoad = await Go(h, h.LoadStopId, StopExecutionStatus.Unloading);

        Assert.Equal(ExecutionOutcome.InvalidState, loadingOnUnload.Outcome);
        Assert.Equal(ExecutionOutcome.InvalidState, unloadingOnLoad.Outcome);
    }

    [Fact]
    public async Task Skip_RequiresReason_AndOnlyBeforeArrival()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noReason = await Go(h, h.LoadStopId, StopExecutionStatus.Skipped);
        Assert.Equal(ExecutionOutcome.ValidationFailed, noReason.Outcome);

        await Go(h, h.LoadStopId, StopExecutionStatus.Arrived);
        var afterArrival = await Go(h, h.LoadStopId, StopExecutionStatus.Skipped, "Poort gesloten");
        Assert.Equal(ExecutionOutcome.InvalidState, afterArrival.Outcome);

        var enRoute = await Go(h, h.UnloadStopId, StopExecutionStatus.EnRoute);
        Assert.Equal(ExecutionOutcome.Success, enRoute.Outcome);
        var skipped = await Go(h, h.UnloadStopId, StopExecutionStatus.Skipped, "Klant heeft afgebeld");
        Assert.Equal(ExecutionOutcome.Success, skipped.Outcome);
        Assert.Equal("Klant heeft afgebeld", StopOf(skipped, h.UnloadStopId).StatusReason);
    }

    [Fact]
    public async Task FailedAndPartiallyCompleted_RequireReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Go(h, h.LoadStopId, StopExecutionStatus.Arrived);
        var failedNoReason = await Go(h, h.LoadStopId, StopExecutionStatus.Failed);
        Assert.Equal(ExecutionOutcome.ValidationFailed, failedNoReason.Outcome);

        var failed = await Go(h, h.LoadStopId, StopExecutionStatus.Failed, "Toegang geweigerd");
        Assert.Equal(ExecutionOutcome.Success, failed.Outcome);
        var failedStop = StopOf(failed, h.LoadStopId);
        Assert.Equal("Toegang geweigerd", failedStop.StatusReason);
        Assert.NotNull(failedStop.DepartedAt);

        await Go(h, h.UnloadStopId, StopExecutionStatus.Unloading);
        var partialNoReason = await Go(h, h.UnloadStopId, StopExecutionStatus.PartiallyCompleted);
        Assert.Equal(ExecutionOutcome.ValidationFailed, partialNoReason.Outcome);
        var partial = await Go(h, h.UnloadStopId, StopExecutionStatus.PartiallyCompleted, "2 van 20 paletten geweigerd");
        Assert.Equal(ExecutionOutcome.Success, partial.Outcome);
    }

    [Fact]
    public async Task Arrive_AfterLatestAllowedBound_RequiresLateArrivalReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stop = await h.Db.Context.TransportOrderStops.FindAsync(h.LoadStopId);
        stop!.ConfirmedFrom = Now.UtcDateTime.AddHours(-3);
        stop.ConfirmedTo = Now.UtcDateTime.AddHours(-1);
        await h.Db.Context.SaveChangesAsync();

        var late = await Go(h, h.LoadStopId, StopExecutionStatus.Arrived);
        Assert.Equal(ExecutionOutcome.ValidationFailed, late.Outcome);

        var explained = await Go(h, h.LoadStopId, StopExecutionStatus.Arrived, "File op de ring");
        Assert.Equal(ExecutionOutcome.Success, explained.Outcome);
        Assert.Equal("File op de ring", StopOf(explained, h.LoadStopId).LateArrivalReason);

        // The unload stop has no window at all: arriving needs no reason.
        var onTime = await Go(h, h.UnloadStopId, StopExecutionStatus.Arrived);
        Assert.Equal(ExecutionOutcome.Success, onTime.Outcome);
        Assert.Null(StopOf(onTime, h.UnloadStopId).LateArrivalReason);
    }

    [Fact]
    public async Task DirectComplete_FromPlanned_RecordsImplicitArrival()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await Go(h, h.LoadStopId, StopExecutionStatus.Completed);

        Assert.Equal(ExecutionOutcome.Success, result.Outcome);
        var stop = StopOf(result, h.LoadStopId);
        Assert.Equal(Now.UtcDateTime, stop.ArrivedAt);
        Assert.Equal(Now.UtcDateTime, stop.CompletedAt);

        var history = await h.Sut.GetStopHistoryAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);
        Assert.Equal(
            new[]
            {
                (StopExecutionStatus.Planned, StopExecutionStatus.Arrived),
                (StopExecutionStatus.Arrived, StopExecutionStatus.Completed),
            },
            history.History!.Select(x => (x.FromStatus, x.ToStatus)).ToArray());
    }

    [Fact]
    public async Task AllTerminalStatuses_AutoCompleteTheTrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Go(h, h.LoadStopId, StopExecutionStatus.EnRoute);
        await Go(h, h.LoadStopId, StopExecutionStatus.Failed, "Pech onderweg");
        var final = await Go(h, h.UnloadStopId, StopExecutionStatus.PartiallyCompleted, "Deel geweigerd");

        Assert.Equal(TripStatus.Completed, final.Execution!.TripStatus);
        Assert.Equal(2, final.Execution.CompletedCount);
    }

    [Fact]
    public async Task ExecutionDto_ExposesWindowsAppointmentAndLocationInstructionFallback()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.GetExecutionAsync(h.TripId, true, CancellationToken.None);

        var load = StopOf(result, h.LoadStopId);
        Assert.Equal(Now.UtcDateTime.AddHours(-2), load.RequestedFrom);
        Assert.Equal(Now.UtcDateTime.AddHours(4), load.RequestedTo);
        Assert.True(load.AppointmentRequired);
        Assert.Equal("SLOT-77", load.AppointmentReference);
        // Stop-level instructions win; otherwise the master location's instructions apply.
        Assert.Equal("Aanmelden aan dok 5", load.LoadingInstructions);
        Assert.Equal("Alfapass verplicht", load.AccessInstructions);

        Assert.Equal(
            new[]
            {
                StopExecutionStatus.EnRoute, StopExecutionStatus.Arrived, StopExecutionStatus.Loading,
                StopExecutionStatus.Loaded, StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted,
                StopExecutionStatus.Skipped,
            },
            load.AllowedTransitions);
    }

    [Fact]
    public async Task History_ForForeignTenant_IsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Go(h, h.LoadStopId, StopExecutionStatus.Arrived);

        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var audit = new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var foreignSync = new TripPlanningSyncService(h.Db.Context, otherTenant);
        var foreign = new TripExecutionService(h.Db.Context, otherTenant, new DevCurrentUserContext(null), audit,
            new TripService(h.Db.Context, otherTenant, audit,
                new PlanningConflictService(h.Db.Context, otherTenant, new QualificationStatusCalculator(), clock),
                new NotificationService(h.Db.Context, otherTenant, new DevCurrentUserContext(null), clock),
                foreignSync, CostingTestFactory.Create(h.Db.Context, otherTenant, clock),
                TripPackageTestFactory.Create(h.Db.Context, otherTenant, clock)),
            foreignSync, TripPackageTestFactory.Create(h.Db.Context, otherTenant, clock),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(h.Db.Context, otherTenant, new DevCurrentUserContext(null), clock), clock);

        var history = await foreign.GetStopHistoryAsync(h.TripId, h.LoadStopId, false, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.NotFound, history.Outcome);
    }

    [Fact]
    public async Task LegacySkipEndpoint_RoutesThroughTheMachine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.SkipAsync(h.TripId, h.LoadStopId, new SkipStopRequest("Locatie gesloten"), true, CancellationToken.None);

        var history = await h.Sut.GetStopHistoryAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);
        var entry = Assert.Single(history.History!);
        Assert.Equal(StopExecutionStatus.Skipped, entry.ToStatus);
        Assert.Equal("Locatie gesloten", entry.Reason);
    }
}
