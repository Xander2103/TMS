using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
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

public class TripExecutionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TripExecutionService Sut, Guid TenantId, Guid TripId,
        Guid OrderId, Guid LoadStopId, Guid UnloadStopId, Guid DriverUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
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
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "chauffeur@acme.be", PasswordHash = "x",
            FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = loadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
            new TransportOrderStop { Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            DriverId = driverId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(userId));
        var planningSync = new TripPlanningSyncService(db.Context, tenant);
        var tripService = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(userId), clock),
            planningSync, CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        var sut = new TripExecutionService(db.Context, tenant, new DevCurrentUserContext(userId), audit, tripService, planningSync,
            TripPackageTestFactory.Create(db.Context, tenant, clock),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(db.Context, tenant, new DevCurrentUserContext(userId), clock), clock);
        return new Harness(db, sut, tenantId, tripId, orderId, loadStopId, unloadStopId, userId);
    }

    [Fact]
    public async Task MyTrips_ResolvesDriverViaUserEmployeeLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var trips = await h.Sut.ListMyTripsAsync(new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 25), CancellationToken.None);

        var single = Assert.Single(trips);
        Assert.Equal("RIT-0001", single.TripNumber);
        Assert.Equal(2, single.StopCount);
        Assert.Equal(0, single.CompletedStopCount);
    }

    [Fact]
    public async Task Execution_ListsStopsInOrder_WithPendingStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: true, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Execution!.TotalCount);
        Assert.All(result.Execution.Stops, s => Assert.Equal(StopExecutionStatus.Planned, s.Status));
        Assert.Equal(StopType.Loading, result.Execution.Stops[0].StopType);
    }

    /// <summary>
    /// M7 (acceptance 2026-10-16): the driver page showed neither "Omschrijving werkzaamheden" of a
    /// crane job nor the goods. The execution DTO now carries the work description + lift data on
    /// the site stop and the goods lines on the goods stops — and never a price.
    /// </summary>
    [Fact]
    public async Task Execution_CarriesTheWorkDescriptionAndGoods_ButNoPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var liftingId = Guid.NewGuid();
        var siteStopId = Guid.NewGuid();
        var customerId = h.Db.Context.Customers.Single().Id;
        h.Db.Context.UnitTypes.Add(new TransportationService.Api.Modules.Reference.Entities.UnitType
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "PAL", Name = "Europallet", IsActive = true,
        });
        h.Db.Context.CargoItems.AddRange(
            new CargoItem
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1, Description = "Kabelhaspels",
                ExpectedQuantity = 4, QuantityUnitCode = "PAL", TotalWeightKg = 3200, LoadingStopId = h.LoadStopId, UnloadingStopId = h.UnloadStopId,
            },
            new CargoItem
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 2, Description = "Klein materiaal",
                ExpectedQuantity = 2, QuantityUnit = "dozen",
            });
        var lifting = new TransportOrder
        {
            Id = liftingId, TenantId = h.TenantId, CustomerId = customerId, OrderNumber = "ORD-0002", OrderDate = new(2026, 7, 21),
            Status = TransportOrderStatus.InProgress, CraneJobKind = CraneJobKind.OnSiteLifting,
            WorkDescription = "Airco-unit op het dak plaatsen", LiftLoadWeightKg = 850, LiftHeightMeters = 18, LiftEquipment = "Hijsbalk 2 t",
            AgreedPrice = 1234.56m, CalculatedPrice = 999.99m, OneOffFixedAmount = 777.77m, OneOffExtraHourlyRate = 88.88m,
        };
        h.Db.Context.TransportOrders.Add(lifting);
        h.Db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = siteStopId, TenantId = h.TenantId, TransportOrderId = liftingId, Sequence = 1, StopType = StopType.Site, City = "Gent",
        });
        h.Db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId, TransportOrderId = liftingId, Sequence = 2 });
        await h.Db.Context.SaveChangesAsync();

        var execution = (await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: true, CancellationToken.None)).Execution!;

        var site = execution.Stops.Single(s => s.TransportOrderStopId == siteStopId);
        Assert.Equal(new ExecutionWorkDto("OnSiteLifting", "Airco-unit op het dak plaatsen", 850m, null, null, 18m, "Hijsbalk 2 t", null), site.Work);
        Assert.Empty(site.Goods!);

        var loading = execution.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        Assert.Null(loading.Work);
        Assert.Equal(
            new[] { new ExecutionGoodsLineDto("Kabelhaspels", 4m, "Europallet", 3200m), new ExecutionGoodsLineDto("Klein materiaal", 2m, "dozen", null) },
            loading.Goods);
        Assert.Equal(3200m, loading.GoodsTotalWeightKg);

        // Never a price on the driver's payload — no field, no amount.
        var json = System.Text.Json.JsonSerializer.Serialize(execution, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        foreach (var forbidden in new[] { "price", "amount", "rate", "tariff", "1234.56", "999.99", "777.77", "88.88" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Arrive_ThenComplete_TracksTimestampsAndPod()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.ArriveAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);
        var completed = await h.Sut.CompleteAsync(h.TripId, h.LoadStopId,
            new CompleteStopRequest("Magazijnier P. Peeters", "Alles geladen"), true, false, CancellationToken.None);

        var stop = completed.Execution!.Stops.Single(s => s.TransportOrderStopId == h.LoadStopId);
        Assert.Equal(StopExecutionStatus.Completed, stop.Status);
        Assert.Equal(Now.UtcDateTime, stop.ArrivedAt);
        Assert.Equal(Now.UtcDateTime, stop.CompletedAt);
        Assert.Equal("Magazijnier P. Peeters", stop.PodSignedBy);
        Assert.Equal(1, completed.Execution.CompletedCount);
    }

    [Fact]
    public async Task CompletingLastStop_AutoCompletesTripAndOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.CompleteAsync(h.TripId, h.LoadStopId, new CompleteStopRequest(null, null), true, false, CancellationToken.None);
        var final = await h.Sut.CompleteAsync(h.TripId, h.UnloadStopId, new CompleteStopRequest("Klant", null), true, false, CancellationToken.None);

        Assert.Equal(TripStatus.Completed, final.Execution!.TripStatus);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    [Fact]
    public async Task Skip_RequiresReason_AndCountsAsHandled()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noReason = await h.Sut.SkipAsync(h.TripId, h.LoadStopId, new SkipStopRequest(""), true, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.ValidationFailed, noReason.Outcome);

        await h.Sut.SkipAsync(h.TripId, h.LoadStopId, new SkipStopRequest("Locatie gesloten"), true, CancellationToken.None);
        var final = await h.Sut.CompleteAsync(h.TripId, h.UnloadStopId, new CompleteStopRequest(null, null), true, false, CancellationToken.None);

        Assert.Equal(TripStatus.Completed, final.Execution!.TripStatus);
    }

    [Fact]
    public async Task ForeignDriver_IsRefused_WhenRestricted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Reassign the trip to a different driver.
        var otherEmployee = Guid.NewGuid();
        var otherDriver = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = otherEmployee, TenantId = h.TenantId, EmployeeNumber = "MED-2",
            FirstName = "Piet", LastName = "Peters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Drivers.Add(new Driver { Id = otherDriver, TenantId = h.TenantId, DriverNumber = "CH-2", EmployeeId = otherEmployee, IsActive = true });
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.DriverId = otherDriver;
        await h.Db.Context.SaveChangesAsync();

        var restricted = await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: true, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.NotYourTrip, restricted.Outcome);

        var dispatcher = await h.Sut.GetExecutionAsync(h.TripId, restrictToOwnDriver: false, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.Success, dispatcher.Outcome);
    }

    [Fact]
    public async Task Mutations_RequireTripInProgress()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.Planned;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.ArriveAsync(h.TripId, h.LoadStopId, true, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.InvalidState, result.Outcome);
    }
}
