using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
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
/// Optimistic concurrency on trips: every mutation bumps Trip.Version; a client echoing a
/// stale version gets StaleVersion (409) with the CURRENT server state instead of silently
/// overwriting another planner's change.
/// </summary>
public class TripConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 07, 21);

    private sealed record Harness(SqliteTestDbContext Db, TripService Sut, Guid TenantId, Guid OrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
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
        var sut = new TripService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        return new Harness(db, sut, tenantId, orderId);
    }

    private static UpdateTripRequest Update(TripDetailDto current, string? notes, Guid? version) =>
        new(current.TripDate, current.DriverId, current.VehicleId, current.TrailerId,
            current.PlannedStart, current.PlannedEnd, notes,
            current.Orders.Select(o => o.TransportOrderId).ToList(), Version: version);

    [Fact]
    public async Task Update_WithCurrentVersion_Succeeds_AndBumpsVersion()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Sut.CreateAsync(
            new CreateTripRequest(TripDate, null, null, null, null, null, null, [h.OrderId]), CancellationToken.None)).Trip!;

        var updated = await h.Sut.UpdateAsync(created.Id, Update(created, "eerste wijziging", created.Version), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, updated.Outcome);
        Assert.NotEqual(created.Version, updated.Trip!.Version);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409WithCurrentState()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Sut.CreateAsync(
            new CreateTripRequest(TripDate, null, null, null, null, null, null, [h.OrderId]), CancellationToken.None)).Trip!;

        // Planner B wins the race.
        var first = await h.Sut.UpdateAsync(created.Id, Update(created, "planner B", created.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, first.Outcome);

        // Planner A still holds the original version.
        var second = await h.Sut.UpdateAsync(created.Id, Update(created, "planner A", created.Version), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.StaleVersion, second.Outcome);
        // The rejection carries the current state so the client can rebase.
        Assert.Equal("planner B", second.Trip!.Notes);
        Assert.Equal(first.Trip!.Version, second.Trip.Version);
    }

    [Fact]
    public async Task ChangeStatus_WithStaleVersion_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Sut.CreateAsync(
            new CreateTripRequest(TripDate, null, null, null, null, null, null, [h.OrderId]), CancellationToken.None)).Trip!;

        var moved = await h.Sut.UpdateAsync(created.Id, Update(created, "gewijzigd", created.Version), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, moved.Outcome);

        var stale = await h.Sut.ChangeStatusAsync(
            created.Id, TripStatus.Cancelled, false, false, null, created.Version, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.StaleVersion, stale.Outcome);

        var fresh = await h.Sut.ChangeStatusAsync(
            created.Id, TripStatus.Cancelled, false, false, null, moved.Trip!.Version, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, fresh.Outcome);
        Assert.Equal(TripStatus.Cancelled, fresh.Trip!.Status);
    }

    [Fact]
    public async Task ChangeStatus_WithoutVersion_StaysBackwardCompatible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Sut.CreateAsync(
            new CreateTripRequest(TripDate, null, null, null, null, null, null, [h.OrderId]), CancellationToken.None)).Trip!;

        // Existing callers (trip auto-complete, older clients) send no version at all.
        var result = await h.Sut.ChangeStatusAsync(
            created.Id, TripStatus.Cancelled, false, false, null, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
    }
}
