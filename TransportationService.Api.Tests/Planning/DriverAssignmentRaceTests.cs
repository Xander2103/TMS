using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Planning;

/// <summary>
/// PERF-H2 (performance/concurrency final remediation 2026-10-21): two planners assigning a driver to
/// the same trip at once. The trip's version token would refuse the loser, but EF sends the
/// planning-entry INSERT before the trip UPDATE, so the loser died on
/// <c>IX_trip_planning_entries_TenantId_TripId</c> with a raw 500. The known-constraint filter now
/// answers 409 with the reload sentence; the winner's assignment is the only one that survives.
/// </summary>
public class DriverAssignmentRaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 10, 22);

    private sealed record Harness(
        SqliteTestDbContext Db, CompetingWriterInterceptor Race, Guid TenantId,
        Guid DriverId, Guid SecondDriverId, Guid VehicleId, Guid OrderId)
    {
        public TripService Trips(TransportationDbContext? context = null)
        {
            var db = context ?? Db.Context;
            var tenant = new DevTenantContext(TenantId);
            var clock = new TestClock(Now);
            return new TripService(db, tenant,
                new AuditService(db, tenant, new DevCurrentUserContext(null)),
                new PlanningConflictService(db, tenant, new QualificationStatusCalculator(), clock),
                new NotificationService(db, tenant, new DevCurrentUserContext(null), clock),
                new TripPlanningSyncService(db, tenant),
                CostingTestFactory.Create(db, tenant, clock),
                TripPackageTestFactory.Create(db, tenant, clock));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var race = new CompetingWriterInterceptor();
        var db = new SqliteTestDbContext(null, race);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var secondEmployeeId = Guid.NewGuid();
        var secondDriverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1 });
        db.Context.Employees.AddRange(
            new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime },
            new Employee { Id = secondEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-2", FirstName = "Piet", LastName = "Peeters", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Drivers.AddRange(
            new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true },
            new Driver { Id = secondDriverId, TenantId = tenantId, DriverNumber = "CH-2", EmployeeId = secondEmployeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 10, 21), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
            Stops =
            [
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, City = "Rotterdam" },
            ],
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, race, tenantId, driverId, secondDriverId, vehicleId, orderId);
    }

    private static UpdateTripRequest Assign(TripDetailDto current, Guid driverId, Guid? version) =>
        new(current.TripDate, driverId, current.VehicleId, current.TrailerId, current.PlannedStart, current.PlannedEnd, current.Notes,
            current.Orders.Select(o => o.TransportOrderId).ToList(), Version: version);

    [Fact]
    public async Task TwoPlannersAssigningADriverAtOnce_TheLoserGets409_NotA500()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Trips().CreateAsync(new CreateTripRequest(
            TripDate, null, h.VehicleId, null, new DateTime(2026, 10, 22, 8, 0, 0), new DateTime(2026, 10, 22, 16, 0, 0), null, [h.OrderId]),
            CancellationToken.None)).Trip!;
        h.Db.Context.ChangeTracker.Clear();

        // Planner B assigns the second driver between A's version check and A's save.
        h.Race.Arm(
            context => context.ChangeTracker.Entries<TripPlanningEntry>().Any(e => e.State == EntityState.Added && e.Entity.TripId == created.Id),
            async cancellationToken =>
            {
                await using var other = h.Db.CreateContextForTenant(h.TenantId);
                var winner = await h.Trips(other).UpdateAsync(created.Id, Assign(created, h.SecondDriverId, created.Version), cancellationToken);
                Assert.Equal(TripOperationOutcome.Success, winner.Outcome);
            });

        // The planning-entry INSERT reaches the unique index before the trip's token is judged.
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            h.Trips().UpdateAsync(created.Id, Assign(created, h.DriverId, created.Version), CancellationToken.None));

        Assert.Equal(1, h.Race.Fired);
        Assert.Equal("IX_trip_planning_entries_TenantId_TripId", UniqueViolations.ConstraintName(exception));

        // The filter turns it into the 409 the planner can act on.
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception,
        };
        new KnownUniqueConflictExceptionFilter().OnException(context);
        var result = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.Contains("De rit is intussen door een andere gebruiker gewijzigd", JsonSerializer.Serialize(result.Value));

        // The winner's assignment is the only one: one live planning entry, the trip carries driver B.
        var entry = Assert.Single(await h.Db.Context.TripPlanningEntries.AsNoTracking().ToListAsync());
        Assert.Equal(h.SecondDriverId, entry.DriverId);
        var trip = await h.Db.Context.Trips.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        Assert.Equal(h.SecondDriverId, trip.DriverId);
    }
}
