using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
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

namespace TransportationService.Api.Tests.Planning;

/// <summary>
/// D1: the fixed vehicle stays on the vehicle side (<c>Vehicle.FixedDriverId</c>); the trip only
/// remembers whether its vehicle was Suggested or chosen by hand (Manual). A Manual vehicle is
/// never replaced by a driver change; a Suggested one is.
/// </summary>
public class VehicleSelectionSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 9, 23);

    private sealed record Harness(
        SqliteTestDbContext Db, TripService Sut, DriverService Drivers, Guid TenantId,
        Guid DriverWithFixedId, Guid OtherDriverWithFixedId, Guid DriverWithoutFixedId,
        Guid FixedVehicleId, Guid OtherFixedVehicleId, Guid PoolVehicleId, Guid OrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1, QualificationExpiryWarningDays = 30,
        });

        Guid AddDriver(string number, string firstName)
        {
            var employeeId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            db.Context.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-" + number,
                FirstName = firstName, LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-" + number, EmployeeId = employeeId, IsActive = true });
            return driverId;
        }

        Guid AddVehicle(string number, Guid? fixedDriverId, bool isActive = true)
        {
            var id = Guid.NewGuid();
            db.Context.Vehicles.Add(new Vehicle
            {
                Id = id, TenantId = tenantId, InternalNumber = "VRT-" + number, LicensePlate = "1-A-" + number,
                IsActive = isActive, FixedDriverId = fixedDriverId,
            });
            return id;
        }

        var withFixed = AddDriver("1", "Jan");
        var otherWithFixed = AddDriver("2", "Piet");
        var withoutFixed = AddDriver("3", "Klaas");
        var fixedVehicle = AddVehicle("1", withFixed);
        var otherFixedVehicle = AddVehicle("2", otherWithFixed);
        var poolVehicle = AddVehicle("3", null);

        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 9, 22), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), clock);
        return new Harness(db, sut, drivers, tenantId, withFixed, otherWithFixed, withoutFixed,
            fixedVehicle, otherFixedVehicle, poolVehicle, orderId);
    }

    private static CreateTripRequest Create(Guid? driverId, Guid? vehicleId, VehicleSelectionSource? source = null) =>
        new(TripDate, driverId, vehicleId, null, null, null, null, [], VehicleSelectionSource: source);

    [Fact]
    public async Task Create_DriverWithFixedVehicle_AndNoVehicle_SuggestsTheFixedVehicle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Create(h.DriverWithFixedId, null), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal(h.FixedVehicleId, result.Trip!.VehicleId);
        Assert.Equal(VehicleSelectionSource.Suggested, result.Trip.VehicleSelectionSource);
        var stored = await h.Db.Context.Trips.AsNoTracking().SingleAsync();
        Assert.Equal(VehicleSelectionSource.Suggested, stored.VehicleSelectionSource);
    }

    [Fact]
    public async Task Create_ExplicitVehicle_IsManual_UnlessTheCallerSaysSuggested()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var manual = await h.Sut.CreateAsync(Create(h.DriverWithFixedId, h.PoolVehicleId), CancellationToken.None);
        var accepted = await h.Sut.CreateAsync(
            Create(h.OtherDriverWithFixedId, h.OtherFixedVehicleId, VehicleSelectionSource.Suggested), CancellationToken.None);

        Assert.Equal(h.PoolVehicleId, manual.Trip!.VehicleId); // the fixed vehicle does NOT override an explicit choice
        Assert.Equal(VehicleSelectionSource.Manual, manual.Trip.VehicleSelectionSource);
        Assert.Equal(VehicleSelectionSource.Suggested, accepted.Trip!.VehicleSelectionSource);
    }

    [Fact]
    public async Task Create_DriverWithoutFixedVehicle_LeavesTheVehicleEmpty()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Create(h.DriverWithoutFixedId, null), CancellationToken.None);

        Assert.Null(result.Trip!.VehicleId);
        Assert.Null(result.Trip.VehicleSelectionSource);
    }

    [Fact]
    public async Task AssignDriver_OnATripWithoutVehicle_SuggestsTheFixedVehicle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = (await h.Sut.CreateAsync(Create(null, null), CancellationToken.None)).Trip!;

        var result = await h.Sut.AssignDriverAsync(trip.Id, new AssignResourceRequest(h.DriverWithFixedId, trip.Version), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal(h.FixedVehicleId, result.Trip!.VehicleId);
        Assert.Equal("VRT-1", result.Trip.VehicleNumber);
        Assert.Equal(VehicleSelectionSource.Suggested, result.Trip.VehicleSelectionSource);
    }

    [Fact]
    public async Task ManualVehicle_SurvivesADriverChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = (await h.Sut.CreateAsync(Create(h.DriverWithoutFixedId, h.PoolVehicleId), CancellationToken.None)).Trip!;

        var result = await h.Sut.AssignDriverAsync(trip.Id, new AssignResourceRequest(h.DriverWithFixedId, trip.Version), CancellationToken.None);

        Assert.Equal(h.DriverWithFixedId, result.Trip!.DriverId);
        Assert.Equal(h.PoolVehicleId, result.Trip.VehicleId);
        Assert.Equal(VehicleSelectionSource.Manual, result.Trip.VehicleSelectionSource);
    }

    [Fact]
    public async Task LegacyVehicleWithoutSource_IsTreatedAsManual()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = (await h.Sut.CreateAsync(Create(h.DriverWithoutFixedId, h.PoolVehicleId), CancellationToken.None)).Trip!;
        var row = await h.Db.Context.Trips.SingleAsync();
        row.VehicleSelectionSource = null; // a trip from before the column existed
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.AssignDriverAsync(trip.Id, new AssignResourceRequest(h.DriverWithFixedId), CancellationToken.None);

        Assert.Equal(h.PoolVehicleId, result.Trip!.VehicleId);
        Assert.Null(result.Trip.VehicleSelectionSource);
    }

    [Fact]
    public async Task SuggestedVehicle_IsReplacedByTheNewDriversFixedVehicle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = (await h.Sut.CreateAsync(Create(h.DriverWithFixedId, null), CancellationToken.None)).Trip!;
        Assert.Equal(h.FixedVehicleId, trip.VehicleId);

        var result = await h.Sut.AssignDriverAsync(trip.Id, new AssignResourceRequest(h.OtherDriverWithFixedId, trip.Version), CancellationToken.None);

        Assert.Equal(h.OtherFixedVehicleId, result.Trip!.VehicleId);
        Assert.Equal(VehicleSelectionSource.Suggested, result.Trip.VehicleSelectionSource);
    }

    [Fact]
    public async Task VehicleEndpoint_SetsManual_UnlessSuggested_AndClearingClearsTheSource()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = (await h.Sut.CreateAsync(Create(h.DriverWithFixedId, null), CancellationToken.None)).Trip!;

        var manual = await h.Sut.AssignVehicleAsync(trip.Id, new AssignResourceRequest(h.PoolVehicleId, trip.Version), CancellationToken.None);
        Assert.Equal(VehicleSelectionSource.Manual, manual.Trip!.VehicleSelectionSource);

        var suggested = await h.Sut.AssignVehicleAsync(trip.Id,
            new AssignResourceRequest(h.FixedVehicleId, manual.Trip.Version, VehicleSelectionSource: VehicleSelectionSource.Suggested),
            CancellationToken.None);
        Assert.Equal(VehicleSelectionSource.Suggested, suggested.Trip!.VehicleSelectionSource);

        var cleared = await h.Sut.AssignVehicleAsync(trip.Id, new AssignResourceRequest(null, suggested.Trip.Version), CancellationToken.None);
        Assert.Null(cleared.Trip!.VehicleId);
        Assert.Null(cleared.Trip.VehicleSelectionSource);
    }

    [Fact]
    public async Task InactiveOrForeignFixedVehicle_IsNeverSuggested()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // The driver's own fixed vehicle is archived; another tenant has a vehicle "fixed" to the same driver id.
        var own = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.FixedVehicleId);
        own.IsActive = false;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, InternalNumber = "X-1", LicensePlate = "9-X-1", IsActive = true,
            FixedDriverId = h.DriverWithFixedId,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.CreateAsync(Create(h.DriverWithFixedId, null), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Null(result.Trip!.VehicleId);

        // Same rule in the driver list the trip page reads.
        var page = await h.Drivers.SearchAsync(null, true, null, null, null, null, null, false, false, PageRequest.Of(1, 50), CancellationToken.None);
        Assert.Null(page.Items.Single(d => d.Id == h.DriverWithFixedId).FixedVehicleId);
    }

    [Fact]
    public async Task AssignVehicle_FromAnotherTenant_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        var foreignVehicleId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicleId, TenantId = otherTenantId, InternalNumber = "X-1", LicensePlate = "9-X-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        var trip = (await h.Sut.CreateAsync(Create(null, null), CancellationToken.None)).Trip!;

        var result = await h.Sut.AssignVehicleAsync(trip.Id,
            new AssignResourceRequest(foreignVehicleId, VehicleSelectionSource: VehicleSelectionSource.Suggested), CancellationToken.None);

        Assert.Equal(TripOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task DriverList_CarriesTheFixedVehicle_SoTheTripPageNeedsNoExtraCall()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var page = await h.Drivers.SearchAsync(null, true, null, null, null, null, null, false, false, PageRequest.Of(1, 50), CancellationToken.None);

        var withFixed = page.Items.Single(d => d.Id == h.DriverWithFixedId);
        Assert.Equal(h.FixedVehicleId, withFixed.FixedVehicleId);
        Assert.Equal("VRT-1", withFixed.FixedVehicleNumber);
        Assert.Equal("1-A-1", withFixed.FixedVehiclePlate);
        var without = page.Items.Single(d => d.Id == h.DriverWithoutFixedId);
        Assert.Null(without.FixedVehicleId);
        Assert.Null(without.FixedVehicleNumber);
    }
}
