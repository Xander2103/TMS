using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Exceptions.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

/// <summary>
/// Wave P9: warehouse day view (readiness per trip, loading stops, no cost/HR surface),
/// package search and dispatcher assignment on exceptions.
/// </summary>
public class WarehouseTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness : IDisposable
    {
        public required SqliteTestDbContext Db { get; init; }
        public required WarehouseService Warehouse { get; init; }
        public required ExecutionExceptionService Exceptions { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid TripId { get; init; }
        public required Guid UserId { get; init; }
        public required Guid PackageId { get; init; }

        public void Dispose() => Db.Dispose()
;    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var loadStopId = Guid.NewGuid();
        var unloadStopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, PackageDepartureRule = "RequireOverride" });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "dispatch@acme.be", PasswordHash = "x",
            FirstName = "Dora", LastName = "Dispatch", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.Planned, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = loadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, LocationName = "Magazijn A", City = "Antwerpen" },
            new TransportOrderStop { Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" });
        db.Context.Trips.AddRange(
            new Trip { Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21), Status = TripStatus.Planned },
            new Trip { Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0002", TripDate = new(2026, 7, 22), Status = TripStatus.Planned },
            new Trip { Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0003", TripDate = new(2026, 7, 21), Status = TripStatus.Draft });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        db.Context.Packages.AddRange(
            new Package
            {
                Id = packageId, TenantId = tenantId, TransportOrderId = orderId,
                PackageNumber = "PKG-00001", BarcodeValue = "PKG-00001-AAAA",
                Description = "Doos elektronica", CurrentLifecycleStatus = PackageLifecycleStatus.Labelled,
            },
            new Package
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
                PackageNumber = "PKG-00002", BarcodeValue = "PKG-00002-AAAA",
                Description = "Pallet stenen", CurrentLifecycleStatus = PackageLifecycleStatus.Loaded,
            });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var currentUser = new DevCurrentUserContext(userId);
        var tripPackages = TripPackageTestFactory.Create(db.Context, tenant, clock);
        var warehouse = new WarehouseService(db.Context, tenant, tripPackages);
        var exceptions = new ExecutionExceptionService(
            db.Context, tenant, currentUser,
            new AuditService(db.Context, tenant, currentUser),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(db.Context, tenant, currentUser, clock),
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(
                Path.Combine(Path.GetTempPath(), $"wh-tests-{Guid.NewGuid():N}")),
            clock);

        return new Harness
        {
            Db = db, Warehouse = warehouse, Exceptions = exceptions,
            TenantId = tenantId, TripId = tripId, UserId = userId, PackageId = packageId,
        };
    }

    [Fact]
    public async Task ListTrips_ReturnsOnlyTheDate_WithReadinessAndLoadingStops()
    {
        using var h = await SeedAsync();

        var trips = await h.Warehouse.ListTripsAsync(new DateOnly(2026, 7, 21), CancellationToken.None);

        // RIT-0002 is another day; RIT-0003 is Draft — neither belongs on the loading floor.
        var trip = Assert.Single(trips);
        Assert.Equal("RIT-0001", trip.TripNumber);
        Assert.Equal(2, trip.TotalPackages);
        Assert.Equal(1, trip.LoadedCount);
        Assert.Equal(1, trip.NotLoadedCount);
        Assert.False(trip.IsComplete);
        Assert.Equal(TransportationService.Api.Modules.Packages.Dtos.PackageDepartureRule.RequireOverride, trip.Rule);
        var stop = Assert.Single(trip.LoadingStops);
        Assert.Equal("Magazijn A", stop.LocationName);
        Assert.Equal(2, stop.ExpectedPackages);
    }

    [Fact]
    public async Task SearchPackages_MatchesNumberBarcodeAndDescription_TenantScoped()
    {
        using var h = await SeedAsync();

        var byNumber = await h.Warehouse.SearchPackagesAsync("pkg-00001", CancellationToken.None);
        Assert.Single(byNumber);
        Assert.Equal("RIT-0001", byNumber[0].TripNumber);

        var byDescription = await h.Warehouse.SearchPackagesAsync("stenen", CancellationToken.None);
        Assert.Single(byDescription);
        Assert.Equal(PackageLifecycleStatus.Loaded, byDescription[0].Status);

        var byBarcode = await h.Warehouse.SearchPackagesAsync("00002-AAAA", CancellationToken.None);
        Assert.Single(byBarcode);

        Assert.Empty(await h.Warehouse.SearchPackagesAsync("x", CancellationToken.None));

        var foreign = new WarehouseService(h.Db.Context, new DevTenantContext(Guid.NewGuid()),
            TripPackageTestFactory.Create(h.Db.Context, new DevTenantContext(Guid.NewGuid()), new TestClock(Now)));
        Assert.Empty(await foreign.SearchPackagesAsync("pkg-00001", CancellationToken.None));
    }

    [Fact]
    public async Task Assign_SetsAndClearsOwner_AndFiltersApply()
    {
        using var h = await SeedAsync();
        h.Db.Context.ExecutionExceptions.Add(new ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            PackageId = h.PackageId, Type = ExecutionExceptionType.DamagedPackage,
            Severity = ExceptionSeverity.High, Status = ExecutionExceptionStatus.Open,
            Description = "Beschadigd", OccurredAt = Now.UtcDateTime,
        });
        h.Db.Context.ExecutionExceptions.Add(new ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            Type = ExecutionExceptionType.Delay, Severity = ExceptionSeverity.Low,
            Status = ExecutionExceptionStatus.Open, Description = "Vertraging", OccurredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        var packageException = await h.Db.Context.ExecutionExceptions.AsNoTracking()
            .FirstAsync(e => e.PackageId != null);

        var assigned = await h.Exceptions.AssignAsync(packageException.Id, h.UserId, CancellationToken.None);
        Assert.Equal("Dora Dispatch", assigned.Exception!.AssignedToName);

        var mine = await h.Exceptions.SearchAsync(null, null, null, null, null, h.UserId, null, null, CancellationToken.None);
        Assert.Single(mine.Items);

        var packagesOnly = await h.Exceptions.SearchAsync(null, null, null, null, true, null, null, null, CancellationToken.None);
        Assert.Single(packagesOnly.Items);
        Assert.Equal("PKG-00001", packagesOnly.Items[0].PackageNumber);

        var byTrip = await h.Exceptions.SearchAsync(null, null, null, h.TripId, null, null, null, null, CancellationToken.None);
        Assert.Equal(2, byTrip.TotalCount);

        var cleared = await h.Exceptions.AssignAsync(packageException.Id, null, CancellationToken.None);
        Assert.Null(cleared.Exception!.AssignedToName);

        var unknownUser = await h.Exceptions.AssignAsync(packageException.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.NotEqual(TransportationService.Api.Modules.Exceptions.Dtos.ExceptionOutcome.Success, unknownUser.Outcome);
    }
}
