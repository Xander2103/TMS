using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// D1: an activity has no driver of its own — its effective assignment is the trip of its order.
/// "Inplannen" creates that trip through the existing trip service, exactly once.
/// </summary>
public class DossierActivityAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId,
        Guid DriverJanId, Guid DriverPietId, Guid JansFixedVehicleId, Guid PoolVehicleId, Guid TrailerId)
    {
        private AuditService Audit(ITenantContext tenant) => new(Db.Context, tenant, new DevCurrentUserContext(null));

        public DossierService Dossiers(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierService(Db.Context, tenant, Audit(tenant), new TestClock(Now));
        }

        public DossierActivityService Activities()
        {
            var tenant = new DevTenantContext(TenantId);
            var orders = new TransportOrderService(Db.Context, tenant, Audit(tenant), new TestClock(Now));
            return new DossierActivityService(Db.Context, tenant, Audit(tenant), Dossiers(), orders, new TestClock(Now));
        }

        public TripService Trips(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var clock = new TestClock(Now);
            return new TripService(Db.Context, tenant, Audit(tenant),
                new PlanningConflictService(Db.Context, tenant, new QualificationStatusCalculator(), clock),
                new NotificationService(Db.Context, tenant, new DevCurrentUserContext(null), clock),
                new TripPlanningSyncService(Db.Context, tenant),
                CostingTestFactory.Create(Db.Context, tenant, clock),
                TripPackageTestFactory.Create(Db.Context, tenant, clock));
        }

        public DossierActivityPlanningService Planning(Guid? tenantId = null) =>
            new(Db.Context, new DevTenantContext(tenantId ?? TenantId), Dossiers(tenantId), Trips(tenantId));

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        public async Task<DossierDetailDto> DossierWithAsync(string typeCode) =>
            await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId, ActivityTypeId: await TypeIdAsync(typeCode)), CancellationToken.None);

        public async Task<DossierDetailDto> AddActivityAsync(DossierDetailDto dossier, string typeCode, bool createOrder) =>
            (await Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
                await TypeIdAsync(typeCode), CreateLinkedOrder: createOrder, Version: dossier.Version), CancellationToken.None))!;

        /// <summary>Only confirmed orders can be placed on a trip (existing planning rule).</summary>
        public async Task ConfirmOrdersAsync()
        {
            foreach (var order in await Db.Context.TransportOrders.Where(o => o.TenantId == TenantId).ToListAsync())
            {
                order.Status = TransportOrderStatus.Confirmed;
            }

            await Db.Context.SaveChangesAsync();
            Db.Context.ChangeTracker.Clear();
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
            TripNumberPrefix = "RIT-", TripNumberNextValue = 1, QualificationExpiryWarningDays = 30,
        });
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV", IsActive = true, DefaultLegalEntityId = entityId,
        });

        Guid AddDriver(Guid forTenant, string number, string firstName)
        {
            var employeeId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            db.Context.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = forTenant, EmployeeNumber = "MED-" + number,
                FirstName = firstName, LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = forTenant, DriverNumber = "CH-" + number, EmployeeId = employeeId, IsActive = true });
            return driverId;
        }

        var jan = AddDriver(tenantId, "1", "Jan");
        var piet = AddDriver(tenantId, "2", "Piet");
        var jansVehicle = Guid.NewGuid();
        var poolVehicle = Guid.NewGuid();
        var trailer = Guid.NewGuid();
        db.Context.Vehicles.Add(new Vehicle { Id = jansVehicle, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1", IsActive = true, FixedDriverId = jan });
        db.Context.Vehicles.Add(new Vehicle { Id = poolVehicle, TenantId = tenantId, InternalNumber = "VRT-2", LicensePlate = "1-A-2", IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailer, TenantId = tenantId, InternalNumber = "OPL-1", LicensePlate = "O-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId, jan, piet, jansVehicle, poolVehicle, trailer);
    }

    private static DossierActivityDto WithOrder(DossierDetailDto dossier, int index = 0) =>
        dossier.Activities!.Where(a => a.LinkedTransportOrderId is not null).ElementAt(index);

    // ------------------------------------------------------------ projection

    [Fact]
    public async Task Assignment_IsNull_WithoutAnOrder_AndWithoutATrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);

        Assert.All(dossier.Activities!, a => Assert.Null(a.Assignment));
        Assert.Contains(dossier.Activities!, a => a.LinkedTransportOrderId is null);
        Assert.Contains(dossier.Activities!, a => a.LinkedTransportOrderId is not null);
    }

    [Fact]
    public async Task Assignment_ProjectsTheTripOfTheOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        await h.ConfirmOrdersAsync();
        var orderId = WithOrder(dossier).LinkedTransportOrderId!.Value;
        var trip = await h.Trips().CreateAsync(
            new CreateTripRequest(new DateOnly(2026, 9, 24), h.DriverPietId, h.PoolVehicleId, h.TrailerId, null, null, null, [orderId]),
            CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, trip.Outcome);

        var detail = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;

        var assignment = WithOrder(detail).Assignment!;
        Assert.Equal(trip.Trip!.Id, assignment.TripId);
        Assert.Equal("RIT-0001", assignment.TripNumber);
        Assert.Equal(new DateOnly(2026, 9, 24), assignment.TripDate);
        Assert.Equal("Draft", assignment.TripStatus);
        Assert.Equal(h.DriverPietId, assignment.DriverId);
        Assert.Equal("Piet Jansen", assignment.DriverName);
        Assert.Equal((h.PoolVehicleId, "VRT-2", "1-A-2"), (assignment.VehicleId!.Value, assignment.VehicleNumber, assignment.VehiclePlate));
        Assert.Equal("Manual", assignment.VehicleSelectionSource);
        Assert.Equal((h.TrailerId, "OPL-1", "O-A-1"), (assignment.TrailerId!.Value, assignment.TrailerNumber, assignment.TrailerPlate));
        Assert.Equal(1, assignment.TripCount);
        Assert.Equal(0, assignment.OtherOrderCount);
        // The standalone activity still has none.
        Assert.Null(detail.Activities!.Single(a => a.LinkedTransportOrderId is null).Assignment);
    }

    [Fact]
    public async Task SharedTrip_ReportsTheOtherOrders_AndCancelledTripsAreIgnored()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        var other = await h.DossierWithAsync("OPSLAG");
        other = await h.AddActivityAsync(other, "DISTRIBUTIE", createOrder: true);
        await h.ConfirmOrdersAsync();
        var orderId = WithOrder(dossier).LinkedTransportOrderId!.Value;
        var otherOrderId = WithOrder(other).LinkedTransportOrderId!.Value;

        // A cancelled trip first: it must neither count nor be chosen.
        var cancelled = await h.Trips().CreateAsync(
            new CreateTripRequest(new DateOnly(2026, 9, 30), null, null, null, null, null, null, [orderId]), CancellationToken.None);
        var cancelResult = await h.Trips().ChangeStatusAsync(cancelled.Trip!.Id, TripStatus.Cancelled, false, false, null, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, cancelResult.Outcome);
        var shared = await h.Trips().CreateAsync(
            new CreateTripRequest(new DateOnly(2026, 9, 24), h.DriverJanId, null, null, null, null, null, [orderId, otherOrderId]),
            CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, shared.Outcome);

        var assignment = WithOrder((await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!).Assignment!;

        Assert.Equal(shared.Trip!.Id, assignment.TripId);
        Assert.Equal(1, assignment.TripCount);
        Assert.Equal(1, assignment.OtherOrderCount);
        Assert.Equal("Suggested", assignment.VehicleSelectionSource); // Jan's fixed vehicle was proposed
        Assert.Equal(h.JansFixedVehicleId, assignment.VehicleId);
    }

    // ------------------------------------------------------------ plan endpoint

    [Fact]
    public async Task Plan_CreatesADraftTripOnce_AndIsIdempotent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        await h.ConfirmOrdersAsync();
        var activity = WithOrder(dossier);

        var planned = (await h.Planning().PlanAsync(dossier.Id, activity.Id,
            new PlanDossierActivityRequest(new DateOnly(2026, 9, 25), DriverId: h.DriverJanId, Version: dossier.Version), CancellationToken.None))!;

        var assignment = WithOrder(planned).Assignment!;
        Assert.Equal("Draft", assignment.TripStatus);
        Assert.Equal("RIT-0001", assignment.TripNumber);
        Assert.Equal(new DateOnly(2026, 9, 25), assignment.TripDate);
        Assert.Equal(h.DriverJanId, assignment.DriverId);
        Assert.Equal(h.JansFixedVehicleId, assignment.VehicleId); // fixed-vehicle proposal from the existing create path
        Assert.Equal("Suggested", assignment.VehicleSelectionSource);

        // Double click / retry, even with other wishes: the existing trip is returned unchanged.
        var again = (await h.Planning().PlanAsync(dossier.Id, activity.Id,
            new PlanDossierActivityRequest(new DateOnly(2026, 10, 1), DriverId: h.DriverPietId), CancellationToken.None))!;

        Assert.Equal(1, await h.Db.Context.Trips.CountAsync());
        var same = WithOrder(again).Assignment!;
        Assert.Equal(assignment.TripId, same.TripId);
        Assert.Equal(h.DriverJanId, same.DriverId);
        Assert.Equal(new DateOnly(2026, 9, 25), same.TripDate);
    }

    [Fact]
    public async Task TwoActivitiesOfOneDossier_GetTwoTrips_WithDifferentDrivers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        dossier = await h.AddActivityAsync(dossier, "DIRECT_TRANSPORT", createOrder: true);
        await h.ConfirmOrdersAsync();
        var first = WithOrder(dossier, 0);
        var second = WithOrder(dossier, 1);

        await h.Planning().PlanAsync(dossier.Id, first.Id, new PlanDossierActivityRequest(new DateOnly(2026, 9, 25), DriverId: h.DriverJanId), CancellationToken.None);
        var result = (await h.Planning().PlanAsync(dossier.Id, second.Id,
            new PlanDossierActivityRequest(new DateOnly(2026, 9, 25), DriverId: h.DriverPietId, VehicleId: h.PoolVehicleId), CancellationToken.None))!;

        Assert.Equal(2, await h.Db.Context.Trips.CountAsync());
        var a1 = result.Activities!.Single(a => a.Id == first.Id).Assignment!;
        var a2 = result.Activities!.Single(a => a.Id == second.Id).Assignment!;
        Assert.NotEqual(a1.TripId, a2.TripId);
        Assert.Equal("Jan Jansen", a1.DriverName);
        Assert.Equal("Piet Jansen", a2.DriverName);
        Assert.Equal("Manual", a2.VehicleSelectionSource);
    }

    [Fact]
    public async Task Plan_TakesTheDateFromTheActivityOrTheOrder_WhenNoneIsGiven()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        await h.ConfirmOrdersAsync();
        var activity = WithOrder(dossier);
        var orderDate = (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == activity.LinkedTransportOrderId)).OrderDate;

        var planned = (await h.Planning().PlanAsync(dossier.Id, activity.Id, new PlanDossierActivityRequest(), CancellationToken.None))!;

        Assert.Equal(activity.PlannedDate ?? orderDate, WithOrder(planned).Assignment!.TripDate);
    }

    [Fact]
    public async Task Plan_StandaloneActivity_IsRefusedInDutch()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var standalone = dossier.Activities!.Single(a => a.LinkedTransportOrderId is null);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Planning().PlanAsync(dossier.Id, standalone.Id, new PlanDossierActivityRequest(), CancellationToken.None));

        Assert.Contains("geen transportopdracht", exception.Message);
        Assert.Equal(0, await h.Db.Context.Trips.CountAsync());
    }

    [Fact]
    public async Task Plan_UnconfirmedOrder_SurfacesTheExistingPlanningRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Planning().PlanAsync(dossier.Id, WithOrder(dossier).Id, new PlanDossierActivityRequest(), CancellationToken.None));

        Assert.Contains("bevestigde opdrachten", exception.Message);
        Assert.Equal(0, await h.Db.Context.Trips.CountAsync());
    }

    [Fact]
    public async Task Plan_StaleDossierVersion_IsAConflict_AndActivityOfAnotherDossierIsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        var other = await h.DossierWithAsync("OPSLAG");
        other = await h.AddActivityAsync(other, "DISTRIBUTIE", createOrder: true);
        await h.ConfirmOrdersAsync();

        await Assert.ThrowsAsync<DossierVersionConflictException>(
            () => h.Planning().PlanAsync(dossier.Id, WithOrder(dossier).Id, new PlanDossierActivityRequest(Version: Guid.NewGuid()), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Planning().PlanAsync(dossier.Id, WithOrder(other).Id, new PlanDossierActivityRequest(), CancellationToken.None));
        Assert.Equal(0, await h.Db.Context.Trips.CountAsync());
    }

    [Fact]
    public async Task Plan_TenantIsolation_OnEveryIncomingId()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "DISTRIBUTIE", createOrder: true);
        await h.ConfirmOrdersAsync();
        var activity = WithOrder(dossier);

        var otherTenantId = Guid.NewGuid();
        var foreignEmployee = Guid.NewGuid();
        var foreignDriver = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        var foreignTrailer = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Employees.Add(new Employee
        {
            Id = foreignEmployee, TenantId = otherTenantId, EmployeeNumber = "X-1", FirstName = "Vreemd", LastName = "Persoon",
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Drivers.Add(new Driver { Id = foreignDriver, TenantId = otherTenantId, DriverNumber = "X-1", EmployeeId = foreignEmployee, IsActive = true });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = otherTenantId, InternalNumber = "X-1", LicensePlate = "9-X-1", IsActive = true });
        h.Db.Context.Trailers.Add(new Trailer { Id = foreignTrailer, TenantId = otherTenantId, InternalNumber = "X-1", LicensePlate = "9-O-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Planning().PlanAsync(
            dossier.Id, activity.Id, new PlanDossierActivityRequest(DriverId: foreignDriver), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Planning().PlanAsync(
            dossier.Id, activity.Id, new PlanDossierActivityRequest(VehicleId: foreignVehicle), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Planning().PlanAsync(
            dossier.Id, activity.Id, new PlanDossierActivityRequest(TrailerId: foreignTrailer), CancellationToken.None));
        // Another tenant cannot plan (or even see) this dossier.
        Assert.Null(await h.Planning(otherTenantId).PlanAsync(dossier.Id, activity.Id, new PlanDossierActivityRequest(), CancellationToken.None));
        Assert.Equal(0, await h.Db.Context.Trips.CountAsync());
    }
}
