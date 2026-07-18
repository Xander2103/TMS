using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
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

public class TripExecutionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TripExecutionService Sut, Guid TenantId, Guid TripId,
        Guid OrderId, Guid LoadStopId, Guid UnloadStopId, Guid DriverUserId);

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
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = loadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
            new TransportOrderStop { Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" });
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
        var tripService = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(userId), clock));
        var sut = new TripExecutionService(db.Context, tenant, new DevCurrentUserContext(userId), audit, tripService, clock);
        return new Harness(db, sut, tenantId, tripId, orderId, loadStopId, unloadStopId, userId);
    }

    [Fact]
    public async Task MyTrips_ResolvesDriverViaUserEmployeeLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var trips = await h.Sut.ListMyTripsAsync(new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 25), CancellationToken.None);

        var single = Assert.Single(trips);
        Assert.Equal("RIT-0001", single.TripNumber);
        Assert.Equal(2, single.StopCount);
        Assert.Equal(0, single.CompletedStopCount);
    }

    [Fact]
    public async Task Execution_ListsStopsInOrder_WithPendingStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: true, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Execution!.TotalCount);
        Assert.All(result.Execution.Stops, s => Assert.Equal(StopExecutionStatus.Pending, s.Status));
        Assert.Equal(StopType.Loading, result.Execution.Stops[0].StopType);
    }

    [Fact]
    public async Task Arrive_ThenComplete_TracksTimestampsAndPod()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.ArriveAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);
        var completed = await h.Sut.CompleteAsync(h.TripId, h.LoadStopId,
            new CompleteStopRequest("Magazijnier P. Peeters", "Alles geladen"), true, CancellationToken.None);

        var stop = completed.Execution!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        Assert.Equal(StopExecutionStatus.Completed, stop.Status);
        Assert.Equal(Now.UtcDateTime, stop.ArrivedAt);
        Assert.Equal(Now.UtcDateTime, stop.CompletedAt);
        Assert.Equal("Magazijnier P. Peeters", stop.PodSignedBy);
        Assert.Equal(1, completed.Execution.CompletedCount);
    }

    [Fact]
    public async Task CompletingLastStop_AutoCompletesTripAndOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.CompleteAsync(h.TripId, h.LoadStopId, new CompleteStopRequest(null, null), true, CancellationToken.None);
        var final = await h.Sut.CompleteAsync(h.TripId, h.UnloadStopId, new CompleteStopRequest("Klant", null), true, CancellationToken.None);

        Assert.Equal(TripStatus.Completed, final.Execution!.TripStatus);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    [Fact]
    public async Task Skip_RequiresReason_AndCountsAsHandled()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noReason = await h.Sut.SkipAsync(h.TripId, h.LoadStopId, new SkipStopRequest(""), true, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.ValidationFailed, noReason.Outcome);

        await h.Sut.SkipAsync(h.TripId, h.LoadStopId, new SkipStopRequest("Locatie gesloten"), true, CancellationToken.None);
        var final = await h.Sut.CompleteAsync(h.TripId, h.UnloadStopId, new CompleteStopRequest(null, null), true, CancellationToken.None);

        Assert.Equal(TripStatus.Completed, final.Execution!.TripStatus);
    }

    [Fact]
    public async Task ForeignDriver_IsRefused_WhenRestricted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Reassign the trip to a different driver.
        var otherEmployee = Guid.NewGuid();
        var otherDriver = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = otherEmployee, TenantId = h.TenantId, EmployeeNumber = "MED-2",
            FirstName = "Piet", LastName = "Peters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Drivers.Add(new Driver { Id = otherDriver, TenantId = h.TenantId, DriverNumber = "CH-2", EmployeeId = otherEmployee, IsActive = true });
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.DriverId = otherDriver;
        await h.Db.Context.SaveChangesAsync();

        var restricted = await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: true, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.NotYourTrip, restricted.Outcome);

        var dispatcher = await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: false, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.Success, dispatcher.Outcome);
    }

    [Fact]
    public async Task Mutations_RequireTripInProgress()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.Planned;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.ArriveAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.InvalidState, result.Outcome);
    }
}
