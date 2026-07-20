using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
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

/// <summary>
/// Planning center: board/unplanned/resources read models and the targeted drag-and-drop
/// commands (incremental order assignment, resource swaps with re-validation and override,
/// rescheduling with driver notifications, concurrency).
/// </summary>
public class PlanningCenterTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 07, 21);

    private sealed record Harness(
        SqliteTestDbContext Db, TripService Trips, PlanningBoardService Board, Guid TenantId,
        Guid DriverId, Guid DriverUserId, Guid SecondDriverId, Guid SecondDriverUserId,
        Guid EmployeeId, Guid VehicleId, Guid SecondVehicleId, Guid TrailerId, Guid CustomerId,
        Guid OrderId, Guid SecondOrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
            QualificationExpiryWarningDays = 30,
        });

        (Guid DriverId, Guid UserId, Guid EmployeeId) AddDriver(string number, string first)
        {
            var employeeId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            db.Context.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = $"MED-{number}",
                FirstName = first, LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            db.Context.Drivers.Add(new Driver
            {
                Id = driverId, TenantId = tenantId, DriverNumber = $"CH-{number}", EmployeeId = employeeId, IsActive = true,
            });
            db.Context.Users.Add(new User
            {
                Id = userId, TenantId = tenantId, Email = $"{first.ToLowerInvariant()}@acme.be", PasswordHash = "x",
                FirstName = first, LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            return (driverId, userId, employeeId);
        }

        var driver1 = AddDriver("1", "Jan");
        var driver2 = AddDriver("2", "Piet");

        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1",
            PayloadKg = 1000, IsActive = true, AdrSuitable = true, HasCrane = true,
        });
        var secondVehicleId = Guid.NewGuid();
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = secondVehicleId, TenantId = tenantId, InternalNumber = "VRT-0002", LicensePlate = "1-A-2",
            PayloadKg = 1000, IsActive = true, AdrSuitable = true,
        });
        db.Context.Trailers.Add(new Trailer
        {
            Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1",
            IsActive = true, AdrSuitable = true,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });

        void AddOrder(Guid id, string number, TransportOrderStatus status, decimal? weight = 500)
        {
            db.Context.TransportOrders.Add(new TransportOrder
            {
                Id = id, TenantId = tenantId, CustomerId = customerId, OrderNumber = number,
                OrderDate = new(2026, 7, 20), Status = status, GoodsDescription = "Paletten", WeightKg = weight,
            });
            db.Context.TransportOrderStops.AddRange(
                new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = id, Sequence = 1,
                    StopType = StopType.Loading, City = "Antwerpen",
                },
                new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = id, Sequence = 2,
                    StopType = StopType.Unloading, City = "Gent",
                });
        }

        AddOrder(orderId, "ORD-0001", TransportOrderStatus.Confirmed);
        AddOrder(secondOrderId, "ORD-0002", TransportOrderStatus.Confirmed, weight: null);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var conflicts = new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock);
        var trips = new TripService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), conflicts,
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        var board = new PlanningBoardService(db.Context, tenant, conflicts, new QualificationStatusCalculator(), clock);
        return new Harness(db, trips, board, tenantId,
            driver1.DriverId, driver1.UserId, driver2.DriverId, driver2.UserId, driver1.EmployeeId,
            vehicleId, secondVehicleId, trailerId, customerId, orderId, secondOrderId);
    }

    private async Task<TripDetailDto> PlannedTripAsync(Harness h)
    {
        var created = (await h.Trips.CreateAsync(
            new CreateTripRequest(TripDate, h.DriverId, h.VehicleId, h.TrailerId, null, null, null, [h.OrderId]),
            CancellationToken.None)).Trip!;
        var planned = await h.Trips.ChangeStatusAsync(created.Id, TripStatus.Planned, false, false, null, CancellationToken.None);
        return planned.Trip!;
    }

    // -----------------------------------------------------------------------
    // Read models
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Board_ProjectsTripsWithCargoCapacityAndConflictCounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await PlannedTripAsync(h);

        var board = await h.Board.GetBoardAsync(TripDate, TripDate, CancellationToken.None);

        var trip = Assert.Single(board.Trips);
        Assert.Equal("RIT-0001", trip.TripNumber);
        Assert.Equal(TripStatus.Planned, trip.Status);
        Assert.Equal("Jan Jansen", trip.DriverName);
        Assert.Equal(1, trip.OrderCount);
        Assert.Equal(2, trip.StopCount);
        Assert.Equal("Antwerpen → Gent", trip.RouteSummary);
        Assert.Equal(500, trip.TotalWeightKg);
        // Capacity source: no trailer capacity configured, trailer assigned → trailer wins and
        // has no numbers; the engine skips, the board shows the trailer's (absent) capacity.
        Assert.Null(trip.CapacityWeightKg);
        Assert.Equal(0, trip.BlockingConflictCount);
        Assert.NotEqual(Guid.Empty, trip.Version);
    }

    [Fact]
    public async Task UnplannedOrders_ExcludeClaimedOrders_AndCarryAttentionBadges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await PlannedTripAsync(h); // claims ORD-0001

        var result = await h.Board.GetUnplannedOrdersAsync(new UnplannedOrdersQuery(), CancellationToken.None);

        var order = Assert.Single(result.Items);
        Assert.Equal("ORD-0002", order.OrderNumber);
        Assert.Contains("MissingWeight", order.AttentionBadges);
        Assert.Equal("Antwerpen", order.FirstLoadingCity);
        Assert.Equal("Gent", order.LastUnloadingCity);

        // Cancelling the trip releases ORD-0001 back into the pool.
        var trip = h.Db.Context.Trips.AsNoTracking().Single();
        await h.Trips.ChangeStatusAsync(trip.Id, TripStatus.Cancelled, false, false, null, CancellationToken.None);
        var after = await h.Board.GetUnplannedOrdersAsync(new UnplannedOrdersQuery(), CancellationToken.None);
        Assert.Equal(2, after.TotalCount);
    }

    [Fact]
    public async Task Resources_ShowAssignmentsAbsencesAndFixedVehicle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await PlannedTripAsync(h);
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Type = AbsenceType.Vacation, StartDate = TripDate.AddDays(1), EndDate = TripDate.AddDays(2),
            Status = AbsenceStatus.Approved,
        });
        var vehicle = h.Db.Context.Vehicles.Single(v => v.Id == h.VehicleId);
        vehicle.FixedDriverId = h.DriverId;
        await h.Db.Context.SaveChangesAsync();

        var resources = await h.Board.GetResourcesAsync(TripDate, TripDate.AddDays(6), CancellationToken.None);

        var jan = resources.Drivers.Single(d => d.Name == "Jan Jansen");
        Assert.Single(jan.Assignments);
        Assert.Single(jan.Absences);
        Assert.Equal("VRT-0001", jan.FixedVehicleNumber);
        var vrt = resources.Vehicles.Single(v => v.InternalNumber == "VRT-0001");
        Assert.Equal("Jan Jansen", vrt.FixedDriverName);
        Assert.Single(vrt.Assignments);
    }

    [Fact]
    public async Task ReadModels_AreTenantScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await PlannedTripAsync(h);

        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var clock = new TestClock(Now);
        var foreignBoard = new PlanningBoardService(h.Db.Context, otherTenant,
            new PlanningConflictService(h.Db.Context, otherTenant, new QualificationStatusCalculator(), clock),
            new QualificationStatusCalculator(), clock);

        Assert.Empty((await foreignBoard.GetBoardAsync(TripDate, TripDate, CancellationToken.None)).Trips);
        Assert.Empty((await foreignBoard.GetUnplannedOrdersAsync(new UnplannedOrdersQuery(), CancellationToken.None)).Items);
        Assert.Empty((await foreignBoard.GetResourcesAsync(TripDate, TripDate, CancellationToken.None)).Drivers);
    }

    // -----------------------------------------------------------------------
    // Targeted commands
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AssignOrders_AppendsToPlannedTrip_AndClaimsOrderStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await PlannedTripAsync(h);

        var result = await h.Trips.AssignOrdersAsync(trip.Id,
            new AssignOrdersRequest([h.SecondOrderId], trip.Version), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Trip!.Orders.Count);
        Assert.Equal([1, 2], result.Trip.Orders.Select(o => o.Sequence).ToArray());
        Assert.Equal(TransportOrderStatus.Planned,
            (await h.Db.Context.TransportOrders.FindAsync(h.SecondOrderId))!.Status);

        // Assigning the same order twice is refused.
        var duplicate = await h.Trips.AssignOrdersAsync(trip.Id,
            new AssignOrdersRequest([h.SecondOrderId], result.Trip.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.ValidationFailed, duplicate.Outcome);
    }

    [Fact]
    public async Task RemoveOrder_ReleasesOrder_ButLastOrderNeedsOverride()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await PlannedTripAsync(h);
        var withTwo = await h.Trips.AssignOrdersAsync(trip.Id,
            new AssignOrdersRequest([h.SecondOrderId], trip.Version), CancellationToken.None);

        var removed = await h.Trips.RemoveOrderAsync(trip.Id, h.SecondOrderId, withTwo.Trip!.Version, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, removed.Outcome);
        Assert.Single(removed.Trip!.Orders);
        Assert.Equal(TransportOrderStatus.Confirmed,
            (await h.Db.Context.TransportOrders.FindAsync(h.SecondOrderId))!.Status);

        // Removing the LAST order creates the blocking NoOrders conflict → refused without override.
        var lastRemoval = await h.Trips.RemoveOrderAsync(trip.Id, h.OrderId, removed.Trip.Version, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.ConflictsBlock, lastRemoval.Outcome);
        Assert.Contains(lastRemoval.Conflicts!, c => c.Code == PlanningConflictCode.NoOrders);
    }

    [Fact]
    public async Task AssignDriver_OnPlannedTrip_NotifiesOldAndNewDriver()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await PlannedTripAsync(h);
        h.Db.Context.Notifications.RemoveRange(h.Db.Context.Notifications); // clear the planning notification
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Trips.AssignDriverAsync(trip.Id,
            new AssignResourceRequest(h.SecondDriverId, trip.Version), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal("Piet Jansen", result.Trip!.DriverName);
        var notifications = h.Db.Context.Notifications.AsNoTracking().ToList();
        Assert.Contains(notifications, n => n.UserId == h.DriverUserId && n.Type == "trip_changed");
        Assert.Contains(notifications, n => n.UserId == h.SecondDriverUserId && n.Type == "trip_assigned");
        Assert.Equal(2, notifications.Count); // exactly once per affected driver
    }

    [Fact]
    public async Task AssignDriver_DoubleBooked_IsBlocked_OverrideNeedsReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await PlannedTripAsync(h); // Jan on TripDate

        // A second trip on the same date, on its own vehicle.
        var second = (await h.Trips.CreateAsync(
            new CreateTripRequest(TripDate, h.SecondDriverId, h.SecondVehicleId, null, null, null, null, [h.SecondOrderId]),
            CancellationToken.None)).Trip!;

        // Moving Jan onto the second trip while he already drives the first (both would be
        // planned) — but second is Draft, so no re-validation runs: assignment succeeds.
        var onDraft = await h.Trips.AssignDriverAsync(second.Id,
            new AssignResourceRequest(h.DriverId, second.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, onDraft.Outcome);

        // Reassigning the PLANNED first trip's vehicle to one that is double-booked is blocked.
        var swap = await h.Trips.AssignDriverAsync(first.Id,
            new AssignResourceRequest(h.SecondDriverId, first.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, swap.Outcome); // Piet is free on the planned level

        // Now plan the second trip so BOTH drivers are committed, then try to give the first
        // trip's driver (Piet) also the second trip's driver slot → double booking, blocked.
        await h.Trips.ChangeStatusAsync(second.Id, TripStatus.Planned, false, false, null, CancellationToken.None);
        var refreshed = (await h.Trips.GetByIdAsync(second.Id, CancellationToken.None))!;
        var blocked = await h.Trips.AssignDriverAsync(second.Id,
            new AssignResourceRequest(h.SecondDriverId, refreshed.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.ConflictsBlock, blocked.Outcome);
        Assert.Contains(blocked.Conflicts!, c => c.Code == PlanningConflictCode.DriverDoubleBooked);

        // Override without reason refused; with reason it lands and leaves a trail.
        var noReason = await h.Trips.AssignDriverAsync(second.Id,
            new AssignResourceRequest(h.SecondDriverId, refreshed.Version, Override: true), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.ValidationFailed, noReason.Outcome);

        var overridden = await h.Trips.AssignDriverAsync(second.Id,
            new AssignResourceRequest(h.SecondDriverId, refreshed.Version, Override: true,
                OverrideReason: "Splitst de rit later op."), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, overridden.Outcome);
        Assert.Contains(h.Db.Context.ConflictOverrides, o => o.EntityId == second.Id);
    }

    [Fact]
    public async Task Reschedule_MovesTrip_SyncsPlanningEntry_AndNotifiesDriver()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await PlannedTripAsync(h);
        h.Db.Context.Notifications.RemoveRange(h.Db.Context.Notifications);
        await h.Db.Context.SaveChangesAsync();

        var newDate = TripDate.AddDays(2);
        var result = await h.Trips.RescheduleAsync(trip.Id,
            new RescheduleTripRequest(newDate, null, null, trip.Version), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal(newDate, result.Trip!.TripDate);
        Assert.Equal(newDate, h.Db.Context.TripPlanningEntries.AsNoTracking().Single().Date);
        Assert.Contains(h.Db.Context.Notifications, n => n.UserId == h.DriverUserId && n.Type == "trip_changed");
    }

    [Fact]
    public async Task TargetedCommands_RejectStaleVersion_AndFinalStatuses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await PlannedTripAsync(h);

        var moved = await h.Trips.RescheduleAsync(trip.Id,
            new RescheduleTripRequest(TripDate.AddDays(1), null, null, trip.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, moved.Outcome);

        var stale = await h.Trips.AssignVehicleAsync(trip.Id,
            new AssignResourceRequest(null, trip.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.StaleVersion, stale.Outcome);

        await h.Trips.ChangeStatusAsync(trip.Id, TripStatus.Cancelled, false, false, null, CancellationToken.None);
        var onCancelled = await h.Trips.AssignVehicleAsync(trip.Id,
            new AssignResourceRequest(null), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.InvalidState, onCancelled.Outcome);
    }

    [Fact]
    public async Task ValidateAssignment_IsDryRun_WithStructuredConflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await PlannedTripAsync(h); // Jan committed on TripDate

        var second = (await h.Trips.CreateAsync(
            new CreateTripRequest(TripDate, null, h.VehicleId, null, null, null, null, [h.SecondOrderId]),
            CancellationToken.None)).Trip!;

        var result = await h.Trips.ValidateAssignmentAsync(second.Id,
            new ValidateAssignmentRequest(DriverId: h.DriverId), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Contains(result.Conflicts!, c =>
            c.Code == PlanningConflictCode.DriverDoubleBooked && c.Severity == ConflictSeverity.Blocking);
        // Nothing was persisted.
        Assert.Null(h.Db.Context.Trips.AsNoTracking().Single(t => t.Id == second.Id).DriverId);
    }
}
