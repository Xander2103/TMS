using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Services;
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
/// Driver-app scoping: the dashboard resolves through the user's driver profile, documents
/// are limited to assets on the driver's active trips, and driver incidents can only link
/// the driver's own work.
/// </summary>
public class DriverAppServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 20);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid DriverUserId, Guid OtherUserId,
        Guid DriverId, Guid TripId, Guid VehicleId, Guid OtherVehicleId, Guid OrderId, string StorageRoot)
    {
        public DriverAppService Sut(Guid userId)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            var clock = new TestClock(Now);
            var audit = new AuditService(Db.Context, tenant, user);
            var planningSync = new TripPlanningSyncService(Db.Context, tenant);
            var trips = new TripService(Db.Context, tenant, audit,
                new PlanningConflictService(Db.Context, tenant, new QualificationStatusCalculator(), clock),
                new NotificationService(Db.Context, tenant, user, clock),
                planningSync, CostingTestFactory.Create(Db.Context, tenant, clock),
                TripPackageTestFactory.Create(Db.Context, tenant, clock));
            var execution = new TripExecutionService(Db.Context, tenant, user, audit, trips, planningSync,
                TripPackageTestFactory.Create(Db.Context, tenant, clock),
                new NotificationService(Db.Context, tenant, user, clock), clock);
            return new DriverAppService(Db.Context, tenant, user, execution,
                new LocalFileStorageService(StorageRoot), clock);
        }

        public DriverIncidentService Incidents(Guid userId)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            var clock = new TestClock(Now);
            return new DriverIncidentService(Db.Context, tenant, user,
                new IncidentService(Db.Context, tenant,
                    new AuditService(Db.Context, tenant, user),
                    new NotificationService(Db.Context, tenant, user, clock), clock),
                new NotificationService(Db.Context, tenant, user, clock));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var otherVehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Users.AddRange(
            new User
            {
                Id = driverUserId, TenantId = tenantId, Email = "jan@acme.be", PasswordHash = "x",
                FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = otherUserId, TenantId = tenantId, Email = "kantoor@acme.be", PasswordHash = "x",
                FirstName = "Ka", LastName = "Ntoor", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Vehicles.AddRange(
            new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1", IsActive = true },
            new Vehicle { Id = otherVehicleId, TenantId = tenantId, InternalNumber = "VRT-2", LicensePlate = "1-A-2", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = Today, Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, Sequence = 1,
            StopType = StopType.Unloading, City = "Gent",
        });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = Today,
            DriverId = driverId, VehicleId = vehicleId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        // A document on the driver's vehicle, and one on an unrelated vehicle.
        db.Context.FleetDocuments.AddRange(
            new FleetDocument
            {
                Id = Guid.NewGuid(), TenantId = tenantId, VehicleId = vehicleId,
                DocumentType = FleetDocumentType.Insurance, DocumentNumber = "VZ-1",
            },
            new FleetDocument
            {
                Id = Guid.NewGuid(), TenantId = tenantId, VehicleId = otherVehicleId,
                DocumentType = FleetDocumentType.Registration, DocumentNumber = "IN-2",
            });
        await db.Context.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-driverapp-tests", Guid.NewGuid().ToString("N"));
        return new Harness(db, tenantId, driverUserId, otherUserId, driverId, tripId, vehicleId, otherVehicleId, orderId, storageRoot);
    }

    [Fact]
    public async Task Dashboard_IsSelfScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var mine = await h.Sut(h.DriverUserId).GetMyDashboardAsync(CancellationToken.None);
        Assert.NotNull(mine);
        Assert.Equal("RIT-0001", mine!.CurrentTrip?.TripNumber);
        Assert.Equal(1, mine.OpenStopCount);
        Assert.Equal("Gent", mine.NextStopCity);
        Assert.Equal(1, mine.TodayTripCount);

        // A user without a driver profile has no dashboard.
        Assert.Null(await h.Sut(h.OtherUserId).GetMyDashboardAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Documents_OnlyCoverAssetsOnOwnActiveTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var documents = await h.Sut(h.DriverUserId).ListMyDocumentsAsync(CancellationToken.None);

        var document = Assert.Single(documents);
        Assert.Equal("VRT-1", document.AssetNumber);
        Assert.Equal(FleetDocumentType.Insurance, document.DocumentType);
        Assert.False(document.FileAvailable); // no file uploaded

        // Downloading the unrelated vehicle's document resolves to null (404), never a leak.
        var foreignDoc = h.Db.Context.FleetDocuments.Single(d => d.VehicleId == h.OtherVehicleId);
        Assert.Null(await h.Sut(h.DriverUserId).OpenMyDocumentAsync(foreignDoc.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DriverIncident_LinksMustBelongToOwnTrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var incidents = h.Incidents(h.DriverUserId);

        // Foreign trip id → rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => incidents.CreateMineAsync(
            new CreateDriverIncidentRequest("Lek", "Band lek", "VehicleBreakdown", "High", TripId: Guid.NewGuid()),
            CancellationToken.None));

        // Vehicle that is not on the linked trip → rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => incidents.CreateMineAsync(
            new CreateDriverIncidentRequest("Lek", "Band lek", "VehicleBreakdown", "High",
                TripId: h.TripId, VehicleId: h.OtherVehicleId),
            CancellationToken.None));

        var created = await incidents.CreateMineAsync(
            new CreateDriverIncidentRequest("Lek", "Band lek op de E17.", "VehicleBreakdown", "High",
                TripId: h.TripId, VehicleId: h.VehicleId, ClientRequestId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(h.DriverId, h.Db.Context.Incidents.Single().DriverId);
        var mine = await incidents.ListMineAsync(CancellationToken.None);
        Assert.Single(mine);

        // A user without a driver profile cannot report or list.
        Assert.Null(await h.Incidents(h.OtherUserId).CreateMineAsync(
            new CreateDriverIncidentRequest("X", "Y", "Other", "Low", CustomTypeName: "Divers"),
            CancellationToken.None));
        Assert.Empty(await h.Incidents(h.OtherUserId).ListMineAsync(CancellationToken.None));
    }
}
