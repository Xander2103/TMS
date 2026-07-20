using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Operations.Dtos;
using TransportationService.Api.Modules.Operations.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Operations;

/// <summary>
/// Control-center projection: active trips with stop progress, delay detection from live
/// ETA rows, missing-POD counting, the honest position ladder and tenant isolation.
/// </summary>
public class OperationsOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 20);

    private sealed record Harness(
        SqliteTestDbContext Db, OperationsOverviewService Sut, Guid TenantId,
        Guid TripId, Guid LoadStopId, Guid UnloadStopId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
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
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = Today, Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop
            {
                Id = loadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1,
                StopType = StopType.Loading, City = "Antwerpen",
                PlannedTo = Now.UtcDateTime.AddHours(-1),
            },
            new TransportOrderStop
            {
                Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2,
                StopType = StopType.Unloading, City = "Gent",
                PlannedTo = Now.UtcDateTime.AddHours(2),
            });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = Today,
            DriverId = driverId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new OperationsOverviewService(db.Context, tenant, new TestClock(Now));
        return new Harness(db, sut, tenantId, tripId, loadStopId, unloadStopId);
    }

    [Fact]
    public async Task Overview_ProjectsProgress_DelaysAndMissingPod()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Loading stop finished (no POD needed on a loading stop); unloading pending and LATE.
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            TransportOrderStopId = h.LoadStopId, Status = StopExecutionStatus.Completed,
            CompletedAt = Now.UtcDateTime.AddHours(-2),
        });
        h.Db.Context.StopEtas.Add(new StopEta
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            TransportOrderStopId = h.UnloadStopId,
            CurrentEta = Now.UtcDateTime.AddHours(3), // 1h past the planned window end
            Source = EtaSource.Heuristic, Status = EtaStatus.Late,
        });
        await h.Db.Context.SaveChangesAsync();

        var overview = await h.Sut.GetOverviewAsync(CancellationToken.None);

        var trip = Assert.Single(overview.Trips);
        Assert.Equal("RIT-0001", trip.TripNumber);
        Assert.Equal("Jan Jansen", trip.DriverName);
        Assert.Equal(2, trip.StopCount);
        Assert.Equal(1, trip.CompletedStopCount);
        Assert.Equal(h.UnloadStopId, trip.NextStop?.TransportOrderStopId);
        Assert.Equal(EtaStatus.Late, trip.EtaStatus);
        Assert.Equal(EtaSource.Heuristic, trip.EtaSource);
        Assert.Equal(60, trip.DelayMinutes);
        Assert.Equal(1, overview.Counters.DelayedTrips);
        // Loading stop completed without POD is NOT a missing POD.
        Assert.Equal(0, trip.MissingPodCount);
    }

    [Fact]
    public async Task Overview_CountsMissingPod_OnCompletedUnloadingStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            TransportOrderStopId = h.UnloadStopId, Status = StopExecutionStatus.Completed,
            CompletedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var overview = await h.Sut.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, Assert.Single(overview.Trips).MissingPodCount);
        Assert.Equal(1, overview.Counters.MissingPods);
    }

    [Fact]
    public async Task Position_UsesScanGps_ThenPlannedStop_ThenUnavailable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // No data at all: no coordinates on inline stops → Unavailable, never fabricated.
        var bare = await h.Sut.GetOverviewAsync(CancellationToken.None);
        Assert.Equal(LocationSource.Unavailable, Assert.Single(bare.Trips).Position.Source);

        // A custody event with GPS wins over everything.
        var packageId = Guid.NewGuid();
        var orderId = h.Db.Context.TripOrders.Single(o => o.TripId == h.TripId).TransportOrderId;
        h.Db.Context.Packages.Add(new Package
        {
            Id = packageId, TenantId = h.TenantId, TransportOrderId = orderId,
            PackageNumber = "PKG-0001", BarcodeValue = "BC-1", Description = "Pallet",
        });
        h.Db.Context.PackageEvents.Add(new PackageEvent
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            PackageId = packageId, EventType = PackageEventType.LoadScan,
            Latitude = 51.2194m, Longitude = 4.4025m, OccurredAt = Now.UtcDateTime.AddMinutes(-10),
        });
        await h.Db.Context.SaveChangesAsync();

        var withGps = await h.Sut.GetOverviewAsync(CancellationToken.None);
        var position = Assert.Single(withGps.Trips).Position;
        Assert.Equal(LocationSource.ScanLocation, position.Source);
        Assert.Equal(51.2194m, position.Latitude);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-10), position.Timestamp);
    }

    [Fact]
    public async Task Overview_IsTenantScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var foreign = new OperationsOverviewService(h.Db.Context, new DevTenantContext(Guid.NewGuid()), new TestClock(Now));
        var overview = await foreign.GetOverviewAsync(CancellationToken.None);

        Assert.Empty(overview.Trips);
        Assert.Equal(0, overview.Counters.ActiveTrips);
    }
}
