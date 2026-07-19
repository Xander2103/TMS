using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
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

public class TripServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 07, 21);

    private sealed record Harness(
        SqliteTestDbContext Db, TripService Sut, Guid TenantId,
        Guid DriverId, Guid EmployeeId, Guid VehicleId, Guid TrailerId, Guid OrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
            QualificationExpiryWarningDays = 30,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1",
            HasCrane = true, AdrSuitable = true, IsActive = true,
        });
        db.Context.Trailers.Add(new Trailer
        {
            Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1",
            AdrSuitable = true, IsActive = true,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var conflicts = new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock);
        var sut = new TripService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), conflicts,
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        return new Harness(db, sut, tenantId, driverId, employeeId, vehicleId, trailerId, orderId);
    }

    private static CreateTripRequest Request(Harness h, params Guid[] orderIds) =>
        new(TripDate, h.DriverId, h.VehicleId, h.TrailerId, null, null, null, orderIds);

    [Fact]
    public async Task Create_ClaimsTripNumber_AndReportsNoConflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal("RIT-0001", result.Trip!.TripNumber);
        Assert.Equal(TripStatus.Draft, result.Trip.Status);
        Assert.Equal("Jan Jansen", result.Trip.DriverName);
        Assert.Empty(result.Trip.Conflicts);
        Assert.Single(result.Trip.Orders);
    }

    [Fact]
    public async Task Create_NonConfirmedOrder_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Db.Context.TransportOrders.FindAsync(h.OrderId);
        order!.Status = TransportOrderStatus.Draft;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Plan_PropagatesOrderStatus_AndRevertReleases()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        var id = trip.Trip!.Id;

        var planned = await h.Sut.ChangeStatusAsync(id, TripStatus.Planned, false, false, null, CancellationToken.None);
        Assert.Equal(TripStatus.Planned, planned.Trip!.Status);
        Assert.Equal(TransportOrderStatus.Planned, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);

        var reverted = await h.Sut.ChangeStatusAsync(id, TripStatus.Draft, false, false, null, CancellationToken.None);
        Assert.Equal(TripStatus.Draft, reverted.Trip!.Status);
        Assert.Equal(TransportOrderStatus.Confirmed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    [Fact]
    public async Task Plan_WithAbsentDriver_IsBlocked_UnlessOverridden()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Type = AbsenceType.Vacation, StartDate = TripDate.AddDays(-1), EndDate = TripDate.AddDays(2),
            Status = AbsenceStatus.Approved,
        });
        await h.Db.Context.SaveChangesAsync();
        var trip = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);

        var blocked = await h.Sut.ChangeStatusAsync(trip.Trip!.Id, TripStatus.Planned, false, false, null, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.ConflictsBlock, blocked.Outcome);
        Assert.Contains(blocked.Conflicts!, c => c.Code == PlanningConflictCode.DriverAbsent);

        var overridden = await h.Sut.ChangeStatusAsync(trip.Trip.Id, TripStatus.Planned, true, false, null, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, overridden.Outcome);
        Assert.Equal(TripStatus.Planned, overridden.Trip!.Status);
    }

    [Fact]
    public async Task Plan_DoubleBookedDriver_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(first.Trip!.Id, TripStatus.Planned, false, false, null, CancellationToken.None);

        // Second confirmed order so the second trip is otherwise valid.
        var secondOrder = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = secondOrder, TenantId = h.TenantId,
            CustomerId = (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.CustomerId,
            OrderNumber = "ORD-0002", OrderDate = new(2026, 7, 20),
            Status = TransportOrderStatus.Confirmed, GoodsDescription = "Meer paletten",
        });
        await h.Db.Context.SaveChangesAsync();

        var second = await h.Sut.CreateAsync(
            new CreateTripRequest(TripDate, h.DriverId, null, null, null, null, null, [secondOrder]), CancellationToken.None);
        var blocked = await h.Sut.ChangeStatusAsync(second.Trip!.Id, TripStatus.Planned, false, false, null, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.ConflictsBlock, blocked.Outcome);
        Assert.Contains(blocked.Conflicts!, c => c.Code == PlanningConflictCode.DriverDoubleBooked);
    }

    [Fact]
    public async Task Plan_CraneOrderOnCranelessVehicle_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vehicle = await h.Db.Context.Vehicles.FindAsync(h.VehicleId);
        vehicle!.HasCrane = false;
        var order = await h.Db.Context.TransportOrders.FindAsync(h.OrderId);
        order!.CraneRequired = true;
        await h.Db.Context.SaveChangesAsync();

        var trip = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        var blocked = await h.Sut.ChangeStatusAsync(trip.Trip!.Id, TripStatus.Planned, false, false, null, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.ConflictsBlock, blocked.Outcome);
        Assert.Contains(blocked.Conflicts!, c => c.Code == PlanningConflictCode.OrderRequiresCrane);
    }

    [Fact]
    public async Task Order_CannotSitOnTwoActiveTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);

        var second = await h.Sut.CreateAsync(
            new CreateTripRequest(TripDate.AddDays(1), null, null, null, null, null, null, [h.OrderId]),
            CancellationToken.None);

        Assert.Equal(TripOperationOutcome.ValidationFailed, second.Outcome);
        Assert.Contains("staat al op rit", second.Error);
    }

    [Fact]
    public async Task Complete_Flow_PropagatesToCompleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        var id = trip.Trip!.Id;

        await h.Sut.ChangeStatusAsync(id, TripStatus.Planned, false, false, null, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(id, TripStatus.InProgress, false, false, null, CancellationToken.None);
        var completed = await h.Sut.ChangeStatusAsync(id, TripStatus.Completed, false, false, null, CancellationToken.None);

        Assert.Equal(TripStatus.Completed, completed.Trip!.Status);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        Assert.Empty(completed.Trip.AllowedTransitions);
    }

    [Fact]
    public async Task Cancel_ReleasesOrders_AndFreesThemForNewTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(trip.Trip!.Id, TripStatus.Planned, false, false, null, CancellationToken.None);

        await h.Sut.ChangeStatusAsync(trip.Trip.Id, TripStatus.Cancelled, false, false, null, CancellationToken.None);
        Assert.Equal(TransportOrderStatus.Confirmed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);

        var next = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, next.Outcome);
    }

    [Fact]
    public async Task Update_DraftOnly_AndValidatesReferences()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Sut.CreateAsync(Request(h, h.OrderId), CancellationToken.None);
        var id = trip.Trip!.Id;

        var foreignVehicle = await h.Sut.UpdateAsync(id,
            new UpdateTripRequest(TripDate, h.DriverId, Guid.NewGuid(), null, null, null, null, [h.OrderId]),
            CancellationToken.None);
        Assert.Equal(TripOperationOutcome.InvalidReference, foreignVehicle.Outcome);

        await h.Sut.ChangeStatusAsync(id, TripStatus.Planned, false, false, null, CancellationToken.None);
        var locked = await h.Sut.UpdateAsync(id,
            new UpdateTripRequest(TripDate, h.DriverId, h.VehicleId, null, null, null, null, [h.OrderId]),
            CancellationToken.None);
        Assert.Equal(TripOperationOutcome.InvalidState, locked.Outcome);
    }

    [Fact]
    public async Task List_ComputesBlockingConflictCount_TenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Trip without driver/vehicle/orders → 3 blocking conflicts.
        await h.Sut.CreateAsync(new CreateTripRequest(TripDate, null, null, null, null, null, null, []), CancellationToken.None);

        var foreignTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, TripNumber = "RIT-X",
            TripDate = TripDate, Status = TripStatus.Draft,
        });
        await h.Db.Context.SaveChangesAsync();

        var list = await h.Sut.ListAsync(TripDate, TripDate, null, null, CancellationToken.None);

        var single = Assert.Single(list);
        Assert.Equal("RIT-0001", single.TripNumber);
        Assert.Equal(3, single.BlockingConflictCount);
    }
}
