using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
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

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Acceptance flow A (confirmation sprint 2026-09-23): a dossier with two transport orders is
/// confirmed automatically by the EXISTING trip-completion path, and only once the last trip is
/// completed. No new event system — <see cref="TripService.ChangeStatusAsync"/> is the trigger.
/// </summary>
public class DossierAutoConfirmationFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 9, 23);

    private sealed record Harness(SqliteTestDbContext Db, TripService Trips, Guid TenantId, Guid DriverId, Guid VehicleId, Guid DossierId, Guid OrderA, Guid OrderB);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1, QualificationExpiryWarningDays = 30 });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Peeters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportDossiers.Add(new TransportDossier { Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-1", Title = "Werf", CustomerId = customerId, Status = DossierStatus.Open });
        var transportType = new ActivityType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "DIRECT_TRANSPORT", Name = "Transport", IsActive = true, HasStops = true, PlanningRelevant = true, IsBillable = true };
        db.Context.ActivityTypes.Add(transportType);
        foreach (var (orderId, number, seq) in new[] { (orderA, "ORD-A", 1), (orderB, "ORD-B", 2) })
        {
            db.Context.TransportOrders.Add(new TransportOrder { Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = number, OrderDate = TripDate, Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten" });
            db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId });
            db.Context.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, ActivityTypeId = transportType.Id, Sequence = seq, LinkedTransportOrderId = orderId });
        }
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var dossiers = new DossierService(db.Context, tenant, audit, clock);
        var lifecycle = new DossierLifecycleService(db.Context, tenant, audit, clock, new DevCurrentUserContext(null), () => dossiers, new DossierReadinessService(db.Context, tenant));
        var trips = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock),
            dossierLifecycle: lifecycle);
        return new Harness(db, trips, tenantId, driverId, vehicleId, dossierId, orderA, orderB);
    }

    private static async Task CompleteTripAsync(Harness h, Guid orderId)
    {
        var created = await h.Trips.CreateAsync(new CreateTripRequest(TripDate, h.DriverId, h.VehicleId, null, null, null, null, [orderId]), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, created.Outcome);
        foreach (var status in new[] { TripStatus.Planned, TripStatus.InProgress, TripStatus.Completed })
        {
            var r = await h.Trips.ChangeStatusAsync(created.Trip!.Id, status, false, false, null, CancellationToken.None);
            Assert.Equal(TripOperationOutcome.Success, r.Outcome);
        }
    }

    [Fact]
    public async Task TheLastCompletedTrip_ConfirmsTheDossierAutomatically_Once()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await CompleteTripAsync(h, h.OrderA);
        var afterFirst = await h.Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == h.DossierId);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == h.OrderA)).Status);
        Assert.Equal(DossierStatus.Open, afterFirst.Status);

        await CompleteTripAsync(h, h.OrderB);
        var afterSecond = await h.Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == h.DossierId);
        Assert.Equal(DossierStatus.Closed, afterSecond.Status);
        Assert.Equal(DossierConfirmationSource.Automatic, afterSecond.ConfirmationSource);
        Assert.Equal(Now.UtcDateTime, afterSecond.ClosedAt);
        Assert.Null(afterSecond.ConfirmedByUserId);

        var audits = await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "TransportDossier" && a.EntityId == h.DossierId.ToString() && a.Action == "ConfirmedAutomatically")
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Contains("trip:RIT-0002", audit.NewValuesJson);
    }
}
