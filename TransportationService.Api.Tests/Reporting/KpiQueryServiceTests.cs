using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Reporting.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Modules.TripCosting.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reporting;

public class KpiQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 07, 20);
    private static readonly DateOnly Today = new(2026, 07, 22);
    private static readonly KpiFilter WeekFilter = new(Monday, Monday.AddDays(4), null, null, null);

    private sealed record Harness(
        SqliteTestDbContext Db, KpiQueryService Sut, Guid TenantId,
        Guid CustomerA, Guid CustomerB, Guid DriverId, Guid VehicleId, Guid Trip1, Guid Trip2);

    /// <summary>
    /// Deterministic week: two completed trips.
    /// Trip1 (Monday): 200 km (50 leeg), order A €1000 + order B €250, final cost €800
    /// (estimated €500), 8h entry actuals, unloading stop completed on time with ETA history
    /// (arrival 10 min late vs snapshot), one failed unloading stop.
    /// Trip2 (Today/Wednesday): 100 km (0 leeg), order A €500, projected cost €300 (not
    /// finalized), planned times 4h, unloading completed late (arrival after window).
    /// One active vehicle → available hours = 5 workdays × 8h = 40h; active 12h → 30%.
    /// Fuel: Monday 80 l €120 diesel. Damage: one open. Exception: one open on Trip1.
    /// </summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var trip1 = Guid.NewGuid();
        var trip2 = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1",
            IsActive = true, FuelType = FuelType.Diesel,
        });
        db.Context.Customers.AddRange(
            new Customer { Id = customerA, TenantId = tenantId, CustomerNumber = "KL-A", Name = "Alpha", IsActive = true },
            new Customer { Id = customerB, TenantId = tenantId, CustomerNumber = "KL-B", Name = "Beta", IsActive = true });

        var orderA1 = Guid.NewGuid();
        var orderB1 = Guid.NewGuid();
        var orderA2 = Guid.NewGuid();
        db.Context.TransportOrders.AddRange(
            new TransportOrder { Id = orderA1, TenantId = tenantId, CustomerId = customerA, OrderNumber = "ORD-A1", OrderDate = Monday, Status = TransportOrderStatus.Completed, GoodsDescription = "x", AgreedPrice = 1000m },
            new TransportOrder { Id = orderB1, TenantId = tenantId, CustomerId = customerB, OrderNumber = "ORD-B1", OrderDate = Monday, Status = TransportOrderStatus.Completed, GoodsDescription = "x", AgreedPrice = 250m },
            new TransportOrder { Id = orderA2, TenantId = tenantId, CustomerId = customerA, OrderNumber = "ORD-A2", OrderDate = Today, Status = TransportOrderStatus.Completed, GoodsDescription = "x", AgreedPrice = 500m });

        db.Context.Trips.AddRange(
            new Trip
            {
                Id = trip1, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = Monday,
                DriverId = driverId, VehicleId = vehicleId, Status = TripStatus.Completed,
                PlannedStart = Monday.ToDateTime(new TimeOnly(8, 0)), PlannedEnd = Monday.ToDateTime(new TimeOnly(15, 0)),
                PlannedDistanceKm = 180m, PlannedEmptyKm = 40m, ActualDistanceKm = 200m, ActualEmptyKm = 50m,
                Orders =
                [
                    new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderA1, Sequence = 1 },
                    new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderB1, Sequence = 2 },
                ],
            },
            new Trip
            {
                Id = trip2, TenantId = tenantId, TripNumber = "RIT-0002", TripDate = Today,
                DriverId = driverId, VehicleId = vehicleId, Status = TripStatus.Completed,
                PlannedStart = Today.ToDateTime(new TimeOnly(8, 0)), PlannedEnd = Today.ToDateTime(new TimeOnly(12, 0)),
                PlannedDistanceKm = 100m,
                Orders = [new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderA2, Sequence = 1 }],
            });

        db.Context.TripCostSummaries.AddRange(
            new TripCostSummary
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip1,
                EstimatedTotal = 500m, ActualTotal = 800m, ProjectedTotal = 800m, Revenue = 1250m,
                IsFinalized = true, FinalCost = 800m, FinalRevenue = 1250m, FinalizedAt = Now.UtcDateTime,
            },
            new TripCostSummary
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip2,
                EstimatedTotal = 350m, ActualTotal = 300m, ProjectedTotal = 300m, Revenue = 500m,
            });

        // Trip1 actuals via the planning entry: 07:30 → 15:30 = 8h. Trip2 uses planned 4h.
        db.Context.TripPlanningEntries.Add(new TripPlanningEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip1, EmployeeId = employeeId, DriverId = driverId,
            TripNumber = "RIT-0001", Date = Monday, Status = TripStatus.Completed,
            ActualStart = Monday.ToDateTime(new TimeOnly(7, 30)), ActualEnd = Monday.ToDateTime(new TimeOnly(15, 30)),
        });

        // Stops: trip1 has one loading + two unloading (one completed on-time-ish, one failed);
        // trip2 one unloading completed but past its window.
        var load1 = Guid.NewGuid();
        var unload1 = Guid.NewGuid();
        var unload1b = Guid.NewGuid();
        var unload2 = Guid.NewGuid();
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = load1, TenantId = tenantId, TransportOrderId = orderA1, Sequence = 1, StopType = StopType.Loading, City = "A" },
            new TransportOrderStop
            {
                Id = unload1, TenantId = tenantId, TransportOrderId = orderA1, Sequence = 2, StopType = StopType.Unloading, City = "B",
                ConfirmedTo = Monday.ToDateTime(new TimeOnly(12, 0)),
            },
            new TransportOrderStop { Id = unload1b, TenantId = tenantId, TransportOrderId = orderB1, Sequence = 1, StopType = StopType.Unloading, City = "C" },
            new TransportOrderStop
            {
                Id = unload2, TenantId = tenantId, TransportOrderId = orderA2, Sequence = 1, StopType = StopType.Unloading, City = "D",
                RequestedTo = Today.ToDateTime(new TimeOnly(10, 0)),
            });
        db.Context.StopExecutions.AddRange(
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip1, TransportOrderStopId = unload1,
                Status = StopExecutionStatus.Completed,
                ArrivedAt = Monday.ToDateTime(new TimeOnly(11, 40)), CompletedAt = Monday.ToDateTime(new TimeOnly(12, 10)),
            },
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip1, TransportOrderStopId = unload1b,
                Status = StopExecutionStatus.Failed, StatusReason = "Klant gesloten",
            },
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip2, TransportOrderStopId = unload2,
                Status = StopExecutionStatus.Completed,
                ArrivedAt = Today.ToDateTime(new TimeOnly(10, 30)), CompletedAt = Today.ToDateTime(new TimeOnly(11, 0)),
            });

        // ETA history for trip1/unload1: snapshot 11:30 before arrival 11:40 → deviation +10 min.
        var eta1 = Guid.NewGuid();
        db.Context.StopEtas.Add(new StopEta
        {
            Id = eta1, TenantId = tenantId, TripId = trip1, TransportOrderStopId = unload1,
            CurrentEta = Monday.ToDateTime(new TimeOnly(11, 30)),
        });
        db.Context.StopEtaHistories.Add(new StopEtaHistory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, StopEtaId = eta1,
            Eta = Monday.ToDateTime(new TimeOnly(11, 30)), RecordedAt = Monday.ToDateTime(new TimeOnly(9, 0)),
        });

        db.Context.FuelTransactions.Add(new FuelTransaction
        {
            Id = Guid.NewGuid(), TenantId = tenantId, VehicleId = vehicleId, DriverId = driverId,
            TransactionDate = Monday, Litres = 80m, TotalAmount = 120m, FullTank = true,
        });
        db.Context.CostRateSets.Add(new CostRateSet
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EffectiveFrom = new DateOnly(2026, 1, 1),
            Co2KgPerLitreDiesel = 2.68m, Co2KgPerLitreOther = 2.31m,
        });
        db.Context.DamageReports.Add(new DamageReport
        {
            Id = Guid.NewGuid(), TenantId = tenantId, VehicleId = vehicleId,
            Description = "Kras", Status = DamageStatus.Reported, Severity = DamageSeverity.Minor,
            IncidentDate = Monday,
        });
        db.Context.ExecutionExceptions.Add(new TransportationService.Api.Modules.Exceptions.Entities.ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip1,
            Type = TransportationService.Api.Modules.Exceptions.Entities.ExecutionExceptionType.Other,
            Status = TransportationService.Api.Modules.Exceptions.Entities.ExecutionExceptionStatus.Open,
            Description = "Wachttijd", OccurredAt = Now.UtcDateTime,
        });

        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new KpiQueryService(db.Context, tenant,
            new CostRateService(db.Context, tenant, audit), new TestClock(Now));
        return new Harness(db, sut, tenantId, customerA, customerB, driverId, vehicleId, trip1, trip2);
    }

    [Fact]
    public async Task Dashboard_ComputesEveryDocumentedKpi()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var kpi = await h.Sut.GetDashboardAsync(WeekFilter, CancellationToken.None);

        // Financieel: revenue 1250 + 500; cost 800 (final) + 300 (projected).
        Assert.Equal(1750m, kpi.RevenuePeriod);
        Assert.Equal(650m, kpi.ProfitPeriod);
        Assert.Equal(500m, kpi.RevenueToday);   // trip2 op woensdag (= vandaag)
        Assert.Equal(200m, kpi.ProfitToday);
        Assert.Equal(37.1m, kpi.AverageMarginPct); // 650/1750
        Assert.Equal(325m, kpi.ProfitPerTrip);
        Assert.Equal(2, kpi.TripCount);

        // Vloot: 200+100 km, 50+0 leeg; 12h actief / 40h beschikbaar = 30%.
        Assert.Equal(300m, kpi.TotalKm);
        Assert.Equal(50m, kpi.EmptyKm);
        Assert.Equal(16.7m, kpi.EmptyKmPct);
        Assert.Equal(30m, kpi.VehicleUtilisationPct);
        Assert.Equal(80m, kpi.FuelLitres);
        Assert.Equal(120m, kpi.FuelCost);
        Assert.Equal(214.4m, kpi.Co2Kg); // 80 l × 2.68
        Assert.Equal(1, kpi.OpenDamageCount);

        // Uitvoering: 3 losstops gepland, 2 succesvol; on-time 1/2; ETA-afwijking +10 min.
        Assert.Equal(66.7m, kpi.DeliveryReliabilityPct);
        Assert.Equal(50m, kpi.OnTimeArrivalPct);
        Assert.Equal(10m, kpi.AvgEtaDeviationMinutes);
        Assert.Equal(1, kpi.FailedDeliveries);
        Assert.Equal(0, kpi.PartialDeliveries);
        Assert.Equal(1, kpi.OpenExceptions);

        // Kostenafwijking: alleen trip1 heeft final (800) + schatting (500) → +60%.
        Assert.Equal(1, kpi.CostOverrunTripCount);
        Assert.Equal(60m, kpi.AvgCostOverrunPct);

        // Verdiepingen.
        var alpha = kpi.TopCustomers.Single(c => c.CustomerName == "Alpha");
        Assert.Equal(1500m, alpha.Revenue); // 1000 + 500
        Assert.Equal(940m, alpha.AllocatedCost); // 800×(1000/1250) + 300×(500/500)
        var beta = kpi.TopCustomers.Single(c => c.CustomerName == "Beta");
        Assert.Equal(250m, beta.Revenue);
        Assert.Equal(160m, beta.AllocatedCost);
        var driver = Assert.Single(kpi.KmPerDriver);
        Assert.Equal(300m, driver.Km);
        Assert.Equal(12m, driver.Hours);
    }

    [Fact]
    public async Task Dashboard_ZeroData_AllSafeDefaults()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Leeg", Slug = "leeg", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var sut = new KpiQueryService(db.Context, tenant,
            new CostRateService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null))),
            new TestClock(Now));

        var kpi = await sut.GetDashboardAsync(WeekFilter, CancellationToken.None);

        Assert.Equal(0m, kpi.RevenuePeriod);
        Assert.Null(kpi.AverageMarginPct);
        Assert.Null(kpi.ProfitPerTrip);
        Assert.Null(kpi.VehicleUtilisationPct);
        Assert.Null(kpi.EmptyKmPct);
        Assert.Null(kpi.DeliveryReliabilityPct);
        Assert.Null(kpi.OnTimeArrivalPct);
        Assert.Null(kpi.AvgEtaDeviationMinutes);
        Assert.Null(kpi.AvgCostOverrunPct);
        Assert.Empty(kpi.TopCustomers);
        Assert.Empty(kpi.KmPerDriver);
    }

    [Fact]
    public async Task CustomerFilter_RestrictsRevenue_AndAllocatesCostByShare()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var kpi = await h.Sut.GetDashboardAsync(WeekFilter with { CustomerId = h.CustomerB }, CancellationToken.None);

        // Beta rijdt alleen mee op trip1: omzet 250, kost 800 × 250/1250 = 160.
        Assert.Equal(250m, kpi.RevenuePeriod);
        Assert.Equal(90m, kpi.ProfitPeriod);
        Assert.Equal(1, kpi.TripCount);
        Assert.Single(kpi.TopCustomers);
    }

    [Fact]
    public async Task DateAndVehicleFilters_Compose()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var mondayOnly = await h.Sut.GetDashboardAsync(
            new KpiFilter(Monday, Monday, null, null, null), CancellationToken.None);
        Assert.Equal(1250m, mondayOnly.RevenuePeriod);
        Assert.Equal(0m, mondayOnly.RevenueToday); // vandaag (woensdag) valt buiten het bereik

        var otherVehicle = await h.Sut.GetDashboardAsync(
            WeekFilter with { VehicleId = Guid.NewGuid() }, CancellationToken.None);
        Assert.Equal(0, otherVehicle.TripCount);
        Assert.Equal(0m, otherVehicle.FuelLitres);
    }

    [Fact]
    public async Task TripProfitability_RowsMatchDefinitions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var rows = await h.Sut.GetTripProfitabilityAsync(WeekFilter, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var trip1 = rows.Single(r => r.TripNumber == "RIT-0001");
        Assert.Equal(1250m, trip1.Revenue);
        Assert.Equal(500m, trip1.EstimatedCost);
        Assert.Equal(800m, trip1.FinalCost);
        Assert.Equal(450m, trip1.Profit);
        Assert.Equal(36m, trip1.MarginPct);
        Assert.Equal(200m, trip1.TotalKm);
        Assert.True(trip1.IsFinalized);
        Assert.Equal("Alpha, Beta", trip1.CustomerNames);
        Assert.Equal("Jan Jansen", trip1.DriverName);
    }

    [Fact]
    public async Task Kpis_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreign = new KpiQueryService(h.Db.Context, foreignTenant,
            new CostRateService(h.Db.Context, foreignTenant,
                new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null))),
            new TestClock(Now));

        var kpi = await foreign.GetDashboardAsync(WeekFilter, CancellationToken.None);

        Assert.Equal(0, kpi.TripCount);
        Assert.Equal(0m, kpi.RevenuePeriod);
        Assert.Equal(0, kpi.OpenDamageCount);
        Assert.Empty(await foreign.GetTripProfitabilityAsync(WeekFilter, CancellationToken.None));
    }
}
