using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.EmployeePlanning;

public class TripPlanningSyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 07, 21);

    private sealed record Harness(
        SqliteTestDbContext Db, TripService Trips, Guid TenantId,
        Guid DriverId, Guid EmployeeId, Guid SecondDriverId, Guid SecondEmployeeId,
        Guid VehicleId, Guid OrderId)
    {
        public TripService ForTenant(Guid tenantId)
        {
            var tenant = new DevTenantContext(tenantId);
            var clock = new TestClock(Now);
            return new TripService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)),
                new PlanningConflictService(Db.Context, tenant, new QualificationStatusCalculator(), clock),
                new NotificationService(Db.Context, tenant, new DevCurrentUserContext(null), clock),
                new TripPlanningSyncService(Db.Context, tenant),
                CostingTestFactory.Create(Db.Context, tenant, clock));
        }

        public IShiftService Shifts()
        {
            var tenant = new DevTenantContext(TenantId);
            var clock = new TestClock(Now);
            return new ShiftService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)),
                new NotificationService(Db.Context, tenant, new DevCurrentUserContext(null), clock),
                new NoOpCalendarSyncService(), clock);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var secondEmployeeId = Guid.NewGuid();
        var secondDriverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = secondEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-2",
            FirstName = "Piet", LastName = "Peeters", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Drivers.Add(new Driver { Id = secondDriverId, TenantId = tenantId, DriverNumber = "CH-2", EmployeeId = secondEmployeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
            Stops =
            [
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, City = "Rotterdam" },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var trips = new TripService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock));
        return new Harness(db, trips, tenantId, driverId, employeeId, secondDriverId, secondEmployeeId, vehicleId, orderId);
    }

    private static CreateTripRequest Create(Harness h, Guid? driverId, params Guid[] orderIds) =>
        new(TripDate, driverId, h.VehicleId, null, new DateTime(2026, 7, 21, 8, 0, 0), new DateTime(2026, 7, 21, 16, 0, 0), "Let op de kade", orderIds);

    [Fact]
    public async Task CreateWithDriver_CreatesLinkedEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, result.Outcome);

        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(result.Trip!.Id, entry.TripId);
        Assert.Equal(h.EmployeeId, entry.EmployeeId);
        Assert.Equal(h.DriverId, entry.DriverId);
        Assert.Equal("Trip", entry.SourceType);
        Assert.Equal(result.Trip.TripNumber, entry.TripNumber);
        Assert.Equal(TripDate, entry.Date);
        Assert.Equal(new DateTime(2026, 7, 21, 8, 0, 0), entry.PlannedStart);
        Assert.Equal("VRT-0001 · 1-A-1", entry.VehicleSummary);
        Assert.Equal("Antwerpen → Rotterdam", entry.RouteSummary);
        Assert.Equal(TripStatus.Draft, entry.Status);
        Assert.Equal("Let op de kade", entry.Notes);

        Assert.Contains(h.Db.Context.AuditLogs.ToList(),
            a => a.EntityType == "TripPlanningEntry" && a.Action == "Created" && a.EntityId == entry.Id.ToString());
    }

    [Fact]
    public async Task CreateWithoutDriver_CreatesNoEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Trips.CreateAsync(Create(h, driverId: null, h.OrderId), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Empty(h.Db.Context.TripPlanningEntries.ToList());
    }

    [Fact]
    public async Task AssigningDriverLater_CreatesEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, driverId: null, h.OrderId), CancellationToken.None);

        var update = new UpdateTripRequest(TripDate, h.DriverId, h.VehicleId, null,
            new DateTime(2026, 7, 21, 8, 0, 0), new DateTime(2026, 7, 21, 16, 0, 0), null, [h.OrderId]);
        var result = await h.Trips.UpdateAsync(created.Trip!.Id, update, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(h.EmployeeId, entry.EmployeeId);
        Assert.Equal(created.Trip.TripNumber, entry.TripNumber);
    }

    [Fact]
    public async Task ChangingDriver_MovesSameRowAtomically()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);
        var entryId = h.Db.Context.TripPlanningEntries.Single().Id;

        var update = new UpdateTripRequest(TripDate, h.SecondDriverId, h.VehicleId, null,
            new DateTime(2026, 7, 21, 8, 0, 0), new DateTime(2026, 7, 21, 16, 0, 0), null, [h.OrderId]);
        var result = await h.Trips.UpdateAsync(created.Trip!.Id, update, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(entryId, entry.Id); // moved, not recreated
        Assert.Equal(h.SecondEmployeeId, entry.EmployeeId);
        Assert.Equal(h.SecondDriverId, entry.DriverId);

        Assert.Contains(h.Db.Context.AuditLogs.ToList(),
            a => a.EntityType == "TripPlanningEntry" && a.Action == "Moved");
    }

    [Fact]
    public async Task ChangingDateAndTimes_UpdatesEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);

        var newDate = TripDate.AddDays(2);
        var update = new UpdateTripRequest(newDate, h.DriverId, h.VehicleId, null,
            new DateTime(2026, 7, 23, 6, 30, 0), new DateTime(2026, 7, 23, 14, 0, 0), null, [h.OrderId]);
        await h.Trips.UpdateAsync(created.Trip!.Id, update, CancellationToken.None);

        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(newDate, entry.Date);
        Assert.Equal(new DateTime(2026, 7, 23, 6, 30, 0), entry.PlannedStart);
        Assert.Equal(new DateTime(2026, 7, 23, 14, 0, 0), entry.PlannedEnd);
    }

    [Fact]
    public async Task RemovingDriver_SoftDeletesEntry_AndReassignResurrectsSameRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);
        var entryId = h.Db.Context.TripPlanningEntries.Single().Id;

        var withoutDriver = new UpdateTripRequest(TripDate, null, h.VehicleId, null, null, null, null, [h.OrderId]);
        await h.Trips.UpdateAsync(created.Trip!.Id, withoutDriver, CancellationToken.None);

        Assert.Empty(h.Db.Context.TripPlanningEntries.ToList());
        var deleted = h.Db.Context.TripPlanningEntries.IgnoreQueryFilters().Single();
        Assert.True(deleted.IsDeleted);

        var withDriver = new UpdateTripRequest(TripDate, h.DriverId, h.VehicleId, null, null, null, null, [h.OrderId]);
        await h.Trips.UpdateAsync(created.Trip.Id, withDriver, CancellationToken.None);

        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(entryId, entry.Id); // resurrected under the unique (tenant, trip) index
        Assert.False(entry.IsDeleted);
    }

    [Fact]
    public async Task CancellingTrip_KeepsEntryAsCancelled()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);

        var result = await h.Trips.ChangeStatusAsync(created.Trip!.Id, TripStatus.Cancelled, false, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(TripStatus.Cancelled, entry.Status);
        Assert.False(entry.IsDeleted);
        Assert.Contains(h.Db.Context.AuditLogs.ToList(),
            a => a.EntityType == "TripPlanningEntry" && a.Action == "Cancelled");
    }

    [Fact]
    public async Task DeletingTrip_SoftDeletesEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);

        await h.Trips.DeleteAsync(created.Trip!.Id, CancellationToken.None);

        Assert.Empty(h.Db.Context.TripPlanningEntries.ToList());
        Assert.True(h.Db.Context.TripPlanningEntries.IgnoreQueryFilters().Single().IsDeleted);
    }

    [Fact]
    public async Task CompletingTrip_ProjectsActualTimesFromStopExecutions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);
        var tripId = created.Trip!.Id;

        await h.Trips.ChangeStatusAsync(tripId, TripStatus.Planned, true, CancellationToken.None);
        await h.Trips.ChangeStatusAsync(tripId, TripStatus.InProgress, false, CancellationToken.None);

        var stopIds = h.Db.Context.TransportOrderStops.Where(s => s.TransportOrderId == h.OrderId)
            .OrderBy(s => s.Sequence).Select(s => s.Id).ToList();
        h.Db.Context.StopExecutions.AddRange(
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = stopIds[0],
                Status = StopExecutionStatus.Completed,
                ArrivedAt = new DateTime(2026, 7, 21, 8, 5, 0), CompletedAt = new DateTime(2026, 7, 21, 9, 0, 0),
                DepartedAt = new DateTime(2026, 7, 21, 9, 10, 0),
            },
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = stopIds[1],
                Status = StopExecutionStatus.Completed,
                ArrivedAt = new DateTime(2026, 7, 21, 11, 0, 0), CompletedAt = new DateTime(2026, 7, 21, 11, 45, 0),
            });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Trips.ChangeStatusAsync(tripId, TripStatus.Completed, false, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        var entry = Assert.Single(h.Db.Context.TripPlanningEntries.ToList());
        Assert.Equal(TripStatus.Completed, entry.Status);
        Assert.Equal(new DateTime(2026, 7, 21, 8, 5, 0), entry.ActualStart);
        Assert.Equal(new DateTime(2026, 7, 21, 11, 45, 0), entry.ActualEnd);
    }

    [Fact]
    public async Task NaiveTimestamps_AreNormalizedToUtc()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // API clients may post ISO times without offset (Kind=Unspecified); PostgreSQL
        // timestamptz would refuse them at save time, so the service must normalize.
        var naive = new CreateTripRequest(TripDate, h.DriverId, h.VehicleId, null,
            DateTime.SpecifyKind(new DateTime(2026, 7, 21, 8, 0, 0), DateTimeKind.Unspecified),
            DateTime.SpecifyKind(new DateTime(2026, 7, 21, 16, 0, 0), DateTimeKind.Unspecified),
            null, [h.OrderId]);
        var result = await h.Trips.CreateAsync(naive, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        var stored = h.Db.Context.Trips.Single(t => t.Id == result.Trip!.Id);
        Assert.Equal(DateTimeKind.Utc, stored.PlannedStart!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.PlannedEnd!.Value.Kind);
    }

    [Fact]
    public async Task SyncTwice_NeverDuplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);

        var update = new UpdateTripRequest(TripDate, h.DriverId, h.VehicleId, null, null, null, "bijgewerkt", [h.OrderId]);
        await h.Trips.UpdateAsync(created.Trip!.Id, update, CancellationToken.None);
        await h.Trips.UpdateAsync(created.Trip.Id, update, CancellationToken.None);

        Assert.Single(h.Db.Context.TripPlanningEntries.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task ForeignTenant_CannotProjectThisTenantsDriver()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreign = h.ForTenant(Guid.NewGuid());

        // The driver reference validation refuses a cross-tenant driver outright.
        var result = await foreign.CreateAsync(Create(h, h.DriverId), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.InvalidReference, result.Outcome);
        Assert.Empty(h.Db.Context.TripPlanningEntries.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task ScheduleGrid_ShowsTripEntry_WithVehicleAndStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);

        var grid = await h.Shifts().GetScheduleAsync(TripDate, TripDate, null, null, CancellationToken.None);

        var row = grid.Rows.Single(r => r.EmployeeId == h.EmployeeId);
        var entry = row.Days.Single().Entries.Single();
        Assert.Equal(ScheduleEntryState.Trip, entry.State);
        Assert.Equal("Trip", entry.SourceType);
        Assert.NotNull(entry.TripId);
        Assert.Equal("VRT-0001 · 1-A-1", entry.VehicleSummary);
        Assert.Equal("Antwerpen → Rotterdam", entry.WorkLocation);
        Assert.Equal("Concept", entry.StatusLabel);
        Assert.Equal(new TimeOnly(8, 0), entry.StartTime);
        // Shifts-only volume: a trip never counts toward planned shift minutes.
        Assert.Equal(0, row.PlannedMinutes);
    }

    [Fact]
    public async Task CancelledTrip_ShowsMutedStateOnGrid()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);
        await h.Trips.ChangeStatusAsync(created.Trip!.Id, TripStatus.Cancelled, false, CancellationToken.None);

        var grid = await h.Shifts().GetScheduleAsync(TripDate, TripDate, null, null, CancellationToken.None);

        var entry = grid.Rows.Single(r => r.EmployeeId == h.EmployeeId).Days.Single().Entries.Single();
        Assert.Equal(ScheduleEntryState.TripCancelled, entry.State);
        Assert.Equal("Geannuleerd", entry.StatusLabel);
    }

    [Fact]
    public async Task EmployeeSelfSchedule_IncludesOwnTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Trips.CreateAsync(Create(h, h.DriverId, h.OrderId), CancellationToken.None);

        var days = await h.Shifts().GetEmployeeScheduleAsync(h.EmployeeId, TripDate, TripDate, CancellationToken.None);
        var otherDays = await h.Shifts().GetEmployeeScheduleAsync(h.SecondEmployeeId, TripDate, TripDate, CancellationToken.None);

        Assert.Single(days.Single().Entries, e => e.SourceType == "Trip");
        Assert.Empty(otherDays.Single().Entries);
    }
}
