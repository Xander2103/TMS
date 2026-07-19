using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Modules.TripCosting.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.TripCosting;

public class TripCostingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 07, 21);

    private sealed record Harness(
        SqliteTestDbContext Db, TripCostingService Sut, CostRateService Rates, Guid TenantId,
        Guid TripId, Guid VehicleId, Guid OrderId, Guid CustomerId, Guid SecondOrderId);

    /// <summary>
    /// Deterministic scenario: 200 km planned (50 empty), 8h planned window, vehicle norm
    /// 30 l/100km, rates: fuel €1.50/l, driver €25/h ×1.2, vehicle €0.50/km + €5/h,
    /// maintenance €0.10/km, depreciation €40/dag, trailer €15/dag, equipment €20/dag
    /// (crane), toll €25, waiting €30/h, overtime ×1.5 boven 8h.
    /// </summary>
    private static async Task<Harness> SeedAsync(bool withRates = true, bool withTrailer = true)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DefaultLoadingMinutes = 30, DefaultUnloadingMinutes = 30,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1",
            IsActive = true, HasCrane = true, ConsumptionLPer100Km = 30m, FuelType = FuelType.Diesel,
        });
        db.Context.Trailers.Add(new Trailer
        {
            Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-1", LicensePlate = "O-A-1", IsActive = true,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.AddRange(
            new TransportOrder
            {
                Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
                OrderDate = TripDate, Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
                AgreedPrice = 1000m,
            },
            new TransportOrder
            {
                Id = secondOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-2",
                OrderDate = TripDate, Status = TransportOrderStatus.Confirmed, GoodsDescription = "Kisten",
                AgreedPrice = 250m,
            });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = TripDate,
            DriverId = driverId, VehicleId = vehicleId, TrailerId = withTrailer ? trailerId : null,
            Status = TripStatus.Draft,
            PlannedStart = TripDate.ToDateTime(new TimeOnly(8, 0)),
            PlannedEnd = TripDate.ToDateTime(new TimeOnly(16, 0)),
            PlannedDistanceKm = 200m, PlannedEmptyKm = 50m,
            Orders =
            [
                new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, Sequence = 1 },
                new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = secondOrderId, Sequence = 2 },
            ],
        });

        if (withRates)
        {
            db.Context.CostRateSets.Add(new CostRateSet
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EffectiveFrom = new DateOnly(2026, 1, 1),
                FuelPricePerLitre = 1.50m, DefaultConsumptionLPer100Km = 25m,
                VehicleCostPerKm = 0.50m, VehicleCostPerHour = 5m,
                DriverCostPerHour = 25m, EmployerCostMultiplier = 1.2m,
                MaintenanceCostPerKm = 0.10m, DepreciationPerDay = 40m,
                TrailerCostPerDay = 15m, EquipmentCostPerDay = 20m,
                DefaultTollPerTrip = 25m, OvertimeThresholdMinutesPerDay = 480, OvertimeRateMultiplier = 1.5m,
                WaitingTimeCostPerHour = 30m,
            });
        }

        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var rates = new CostRateService(db.Context, tenant, audit);
        var sut = new TripCostingService(db.Context, tenant, new DevCurrentUserContext(null), audit, rates, clock);
        return new Harness(db, sut, rates, tenantId, tripId, vehicleId, orderId, customerId, secondOrderId);
    }

    private static decimal AmountOf(TripCostingDto costing, TripCostPhase phase, TripCostType type) =>
        costing.Lines.Where(l => l.Phase == phase && l.CostType == type).Sum(l => l.Amount);

    [Fact]
    public async Task Estimate_ComputesEveryComponent_FromVehicleNormAndRates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        Assert.Equal(CostingOutcome.Success, result.Outcome);
        var costing = result.Costing!;
        // Fuel: 200 km × 30 l/100km = 60 l × €1.50 = €90.
        Assert.Equal(90m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Fuel));
        // Driver: 8 h × €25 × 1.2 = €240.
        Assert.Equal(240m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.DriverLabour));
        // Vehicle km: 200 × €0.50 = €100; vehicle uur: 8 × €5 = €40.
        Assert.Equal(100m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.VehicleDistance));
        Assert.Equal(40m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.VehicleTime));
        // Maintenance: 200 × €0.10 = €20; depreciation €40; trailer €15; equipment (kraan) €20; toll €25.
        Assert.Equal(20m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Maintenance));
        Assert.Equal(40m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Depreciation));
        Assert.Equal(15m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Trailer));
        Assert.Equal(20m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Equipment));
        Assert.Equal(25m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Toll));
        Assert.Equal(590m, costing.EstimatedTotal);
    }

    [Fact]
    public async Task Estimate_WithoutVehicleNorm_FallsBackToRateCardConsumption()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vehicle = await h.Db.Context.Vehicles.FindAsync(h.VehicleId);
        vehicle!.ConsumptionLPer100Km = null;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        // 200 km × 25 l/100km (rate-card default) = 50 l × €1.50 = €75.
        Assert.Equal(75m, AmountOf(result.Costing!, TripCostPhase.Estimated, TripCostType.Fuel));
    }

    [Fact]
    public async Task Estimate_MissingInputs_ProducesNoLinesForThoseComponents()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.PlannedDistanceKm = null;
        trip.PlannedStart = null;
        trip.PlannedEnd = null;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        var costing = result.Costing!;
        Assert.Equal(0m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.Fuel));
        Assert.Equal(0m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.DriverLabour));
        Assert.Equal(0m, AmountOf(costing, TripCostPhase.Estimated, TripCostType.VehicleDistance));
        // Day-based components still apply: depreciation + trailer + equipment + toll.
        Assert.Equal(100m, costing.EstimatedTotal);
    }

    [Fact]
    public async Task Estimate_WithoutRateCard_ProducesNoLines()
    {
        var h = await SeedAsync(withRates: false);
        using var _ = h.Db;

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        Assert.Equal(CostingOutcome.Success, result.Outcome);
        Assert.Empty(result.Costing!.Lines);
        Assert.Equal(0m, result.Costing.EstimatedTotal);
    }

    [Fact]
    public async Task Estimate_RefusedOnceTripStarted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.InProgress;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        Assert.Equal(CostingOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Actual_PrefersFuelRecords_AndComputesOvertimeAndWaiting()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.Completed;
        trip.ActualDistanceKm = 250m;
        await h.Db.Context.SaveChangesAsync();

        // Tankbeurt on the trip date: €120 for 80 l.
        h.Db.Context.FuelTransactions.Add(new FuelTransaction
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId,
            TransactionDate = TripDate, Litres = 80m, TotalAmount = 120m, FullTank = true,
        });

        // Execution 07:00 → 17:30 (10.5 h → 2.5 h overtime); unloading dwell 90 min → 60 min waiting.
        var stops = h.Db.Context.TransportOrderStops.Where(s => s.TransportOrderId == h.OrderId).ToList();
        var loadStop = new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1,
            StopType = StopType.Loading, City = "Antwerpen",
        };
        var unloadStop = new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 2,
            StopType = StopType.Unloading, City = "Rotterdam",
        };
        Assert.Empty(stops); // seeded orders carry no stops yet
        h.Db.Context.TransportOrderStops.AddRange(loadStop, unloadStop);
        h.Db.Context.StopExecutions.AddRange(
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId, TransportOrderStopId = loadStop.Id,
                Status = StopExecutionStatus.Completed,
                ArrivedAt = TripDate.ToDateTime(new TimeOnly(7, 0)),
                CompletedAt = TripDate.ToDateTime(new TimeOnly(7, 30)),
            },
            new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId, TransportOrderStopId = unloadStop.Id,
                Status = StopExecutionStatus.Completed,
                ArrivedAt = TripDate.ToDateTime(new TimeOnly(16, 0)),
                CompletedAt = TripDate.ToDateTime(new TimeOnly(17, 30)),
                DepartedAt = TripDate.ToDateTime(new TimeOnly(17, 30)),
            });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Actual, CancellationToken.None);

        var costing = result.Costing!;
        var fuelLine = costing.Lines.Single(l => l.Phase == TripCostPhase.Actual && l.CostType == TripCostType.Fuel);
        Assert.Equal("Tankbeurten", fuelLine.Source);
        Assert.Equal(120m, fuelLine.Amount);
        // Duration 07:00→17:30 = 10.5 h: labour 10.5 × 25 × 1.2 = 315.
        Assert.Equal(315m, AmountOf(costing, TripCostPhase.Actual, TripCostType.DriverLabour));
        // Overtime 2.5 h × 25 × 0.5 × 1.2 = 37.50.
        Assert.Equal(37.50m, AmountOf(costing, TripCostPhase.Actual, TripCostType.Overtime));
        // Waiting: load 30-30=0; unload 90-30=60 min → 1 h × €30.
        Assert.Equal(30m, AmountOf(costing, TripCostPhase.Actual, TripCostType.WaitingTime));
        // Distance-based on actual 250 km: vehicle 125, maintenance 25.
        Assert.Equal(125m, AmountOf(costing, TripCostPhase.Actual, TripCostType.VehicleDistance));
        Assert.Equal(25m, AmountOf(costing, TripCostPhase.Actual, TripCostType.Maintenance));
    }

    [Fact]
    public async Task Projected_MergesActualOverEstimatedPerCostType()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.InProgress;
        await h.Db.Context.SaveChangesAsync();

        // One manual ACTUAL toll line of €60 replaces the €25 estimate for Toll only.
        var result = await h.Sut.AddManualLineAsync(h.TripId,
            new AddCostLineRequest(TripCostPhase.Actual, TripCostType.Toll, "Maut Duitsland", 1, "forfait", 60m),
            CancellationToken.None);

        var costing = result.Costing!;
        Assert.Equal(590m, costing.EstimatedTotal);
        Assert.Equal(60m, costing.ActualTotal);
        // Projected = estimated (590) − estimated toll (25) + actual toll (60) = 625.
        Assert.Equal(625m, costing.ProjectedTotal);
    }

    [Fact]
    public async Task Override_SurvivesRecalculation_AndKeepsReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);
        var fuelLine = first.Costing!.Lines.Single(l => l.CostType == TripCostType.Fuel);

        var overridden = await h.Sut.OverrideLineAsync(h.TripId, fuelLine.Id,
            new OverrideCostLineRequest(111m, "Vaste brandstofafspraak"), CancellationToken.None);
        Assert.Equal(CostingOutcome.Success, overridden.Outcome);

        var recalculated = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        var survivor = recalculated.Costing!.Lines.Single(l => l.CostType == TripCostType.Fuel);
        Assert.Equal(fuelLine.Id, survivor.Id);
        Assert.Equal(111m, survivor.Amount);
        Assert.True(survivor.IsManualOverride);
        Assert.Equal("Vaste brandstofafspraak", survivor.OverrideReason);
    }

    [Fact]
    public async Task Override_RequiresReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);
        var line = first.Costing!.Lines.First();

        var result = await h.Sut.OverrideLineAsync(h.TripId, line.Id, new OverrideCostLineRequest(1m, " "), CancellationToken.None);

        Assert.Equal(CostingOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task ManualLine_AddAndDelete_CalculatedLinesUndeletable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        var added = await h.Sut.AddManualLineAsync(h.TripId,
            new AddCostLineRequest(TripCostPhase.Estimated, TripCostType.FerryTunnelParking, "Ferry Calais", 1, "stuk", 85m),
            CancellationToken.None);
        var manualLine = added.Costing!.Lines.Single(l => l.Source == "Handmatig");
        Assert.Equal(675m, added.Costing.EstimatedTotal); // 590 + 85

        var calculated = added.Costing.Lines.First(l => l.Source == "Berekend");
        var refused = await h.Sut.DeleteLineAsync(h.TripId, calculated.Id, CancellationToken.None);
        Assert.Equal(CostingOutcome.InvalidState, refused.Outcome);

        var deleted = await h.Sut.DeleteLineAsync(h.TripId, manualLine.Id, CancellationToken.None);
        Assert.Equal(590m, deleted.Costing!.EstimatedTotal);
    }

    [Fact]
    public async Task NegativeManualLine_OnlyAllowedAsCorrection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var refused = await h.Sut.AddManualLineAsync(h.TripId,
            new AddCostLineRequest(TripCostPhase.Actual, TripCostType.Manual, "Foute regel", 1, "stuk", -50m),
            CancellationToken.None);
        Assert.Equal(CostingOutcome.ValidationFailed, refused.Outcome);

        var correction = await h.Sut.AddManualLineAsync(h.TripId,
            new AddCostLineRequest(TripCostPhase.Actual, TripCostType.Correction, "Creditnota tol", 1, "stuk", -50m),
            CancellationToken.None);
        Assert.Equal(CostingOutcome.Success, correction.Outcome);
    }

    [Fact]
    public async Task Finalize_FreezesTotals_AgainstLaterRateChanges_AndBlocksMutation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.Completed;
        await h.Db.Context.SaveChangesAsync();

        var finalized = await h.Sut.FinalizeAsync(h.TripId, CancellationToken.None);
        Assert.Equal(CostingOutcome.Success, finalized.Outcome);
        var finalCost = finalized.Costing!.FinalCost;
        Assert.NotNull(finalCost);
        Assert.Equal(1250m, finalized.Costing.Profitability!.Revenue);

        // A later rate change must not touch the frozen numbers.
        var rateSet = h.Db.Context.CostRateSets.Single();
        rateSet.FuelPricePerLitre = 9.99m;
        await h.Db.Context.SaveChangesAsync();

        var after = await h.Sut.GetAsync(h.TripId, true, CancellationToken.None);
        Assert.Equal(finalCost, after!.FinalCost);
        Assert.True(after.IsFinalized);

        var recalcRefused = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Actual, CancellationToken.None);
        Assert.Equal(CostingOutcome.InvalidState, recalcRefused.Outcome);
        var lineRefused = await h.Sut.AddManualLineAsync(h.TripId,
            new AddCostLineRequest(TripCostPhase.Actual, TripCostType.Manual, "Te laat", 1, "stuk", 10m), CancellationToken.None);
        Assert.Equal(CostingOutcome.InvalidState, lineRefused.Outcome);

        // Reopen restores mutability.
        var reopened = await h.Sut.ReopenAsync(h.TripId, CancellationToken.None);
        Assert.Equal(CostingOutcome.Success, reopened.Outcome);
        Assert.False(reopened.Costing!.IsFinalized);
    }

    [Fact]
    public async Task HistoricalRates_ResolveByTripDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // A newer, more expensive card effective AFTER the trip date must not apply.
        h.Db.Context.CostRateSets.Add(new CostRateSet
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EffectiveFrom = new DateOnly(2026, 8, 1),
            FuelPricePerLitre = 5m, DefaultConsumptionLPer100Km = 25m, EmployerCostMultiplier = 1.2m,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        // Still the January card: 60 l × €1.50 = €90 (not ×€5).
        Assert.Equal(90m, AmountOf(result.Costing!, TripCostPhase.Estimated, TripCostType.Fuel));
    }

    [Fact]
    public async Task Profitability_AllocatesCostByRevenueShare_AcrossOrders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        var costing = await h.Sut.GetAsync(h.TripId, true, CancellationToken.None);

        var profitability = costing!.Profitability!;
        Assert.Equal(1250m, profitability.Revenue); // 1000 + 250
        Assert.Equal(590m, profitability.Cost);
        Assert.Equal(660m, profitability.GrossProfit);
        Assert.Equal(52.8m, profitability.MarginPct);
        // Per km (planned 200): revenue 6.25, cost 2.95. Per hour (8h): revenue 156.25, cost 73.75.
        Assert.Equal(6.25m, profitability.RevenuePerKm);
        Assert.Equal(2.95m, profitability.CostPerKm);
        Assert.Equal(156.25m, profitability.RevenuePerHour);
        Assert.Equal(73.75m, profitability.CostPerHour);
        // Allocation 80/20 by revenue share.
        var big = profitability.PerOrder.Single(o => o.OrderNumber == "ORD-1");
        Assert.Equal(472m, big.AllocatedCost);
        Assert.Equal(528m, big.Profit);
        var small = profitability.PerOrder.Single(o => o.OrderNumber == "ORD-2");
        Assert.Equal(118m, small.AllocatedCost);
    }

    [Fact]
    public async Task Profitability_ZeroRevenue_SplitsEquallyAndNullsMargin()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        foreach (var order in h.Db.Context.TransportOrders.ToList())
        {
            order.AgreedPrice = null;
        }

        await h.Db.Context.SaveChangesAsync();
        await h.Sut.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None);

        var costing = await h.Sut.GetAsync(h.TripId, true, CancellationToken.None);

        var profitability = costing!.Profitability!;
        Assert.Equal(0m, profitability.Revenue);
        Assert.Null(profitability.MarginPct);
        Assert.Equal(-590m, profitability.GrossProfit);
        Assert.All(profitability.PerOrder, o => Assert.Equal(295m, o.AllocatedCost));
    }

    [Fact]
    public async Task Actuals_Validated_AndTriggerActualRecalcWhenRunning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var invalid = await h.Sut.UpdateActualsAsync(h.TripId,
            new UpdateTripActualsRequest(100m, 150m), CancellationToken.None);
        Assert.Equal(CostingOutcome.ValidationFailed, invalid.Outcome);

        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.InProgress;
        await h.Db.Context.SaveChangesAsync();

        var updated = await h.Sut.UpdateActualsAsync(h.TripId,
            new UpdateTripActualsRequest(300m, 80m), CancellationToken.None);

        Assert.Equal(CostingOutcome.Success, updated.Outcome);
        Assert.Equal(300m, updated.Costing!.ActualDistanceKm);
        // Actual vehicle-km line follows the new distance: 300 × €0.50 = €150.
        Assert.Equal(150m, AmountOf(updated.Costing, TripCostPhase.Actual, TripCostType.VehicleDistance));
    }

    [Fact]
    public async Task ForeignTenant_SeesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var audit = new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null));
        var foreign = new TripCostingService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null), audit,
            new CostRateService(h.Db.Context, foreignTenant, audit), new TestClock(Now));

        Assert.Null(await foreign.GetAsync(h.TripId, true, CancellationToken.None));
        Assert.Equal(CostingOutcome.NotFound,
            (await foreign.RecalculateAsync(h.TripId, TripCostPhase.Estimated, CancellationToken.None)).Outcome);
    }
}
