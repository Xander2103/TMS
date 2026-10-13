using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Planning;

public class CapacityConflictTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, PlanningConflictService Sut, Guid TenantId, Guid VehicleId, Guid OrderId);

    private static async Task<Harness> SeedAsync(
        decimal? payloadKg = 10000, decimal? volumeM3 = 50,
        decimal? orderWeight = 8000, decimal? orderVolume = 30,
        string capacitySeverity = "Warning")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CapacityConflictSeverity = capacitySeverity,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1",
            OperationalStatus = VehicleOperationalStatus.Available, IsActive = true,
            PayloadKg = payloadKg, VolumeM3 = volumeM3,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.Confirmed,
            WeightKg = orderWeight, VolumeM3 = orderVolume,
        });
        await db.Context.SaveChangesAsync();

        var sut = new PlanningConflictService(db.Context, new DevTenantContext(tenantId),
            new QualificationStatusCalculator(), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId, orderId);
    }

    private static Trip TripFor(Harness h) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1",
        TripDate = new DateOnly(2026, 7, 20), VehicleId = h.VehicleId, Status = TripStatus.Draft,
        Orders = [new TripOrder { TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1 }],
    };

    [Fact]
    public async Task LoadThatFits_ProducesNoCapacityConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code is PlanningConflictCode.CapacityExceeded or PlanningConflictCode.CapacityCheckIncomplete);
    }

    [Fact]
    public async Task ExceedingWeight_WarnsByDefault_WithTotalsInTheMessage()
    {
        var h = await SeedAsync(payloadKg: 10000, orderWeight: 12500);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.CapacityExceeded);
        Assert.False(conflict.Blocking);
        Assert.Contains("12500", conflict.Description.Replace(".", string.Empty).Replace(",", string.Empty));
        Assert.Contains("laadvermogen", conflict.Description);
    }

    [Fact]
    public async Task ExceedingCapacity_Blocks_WhenTenantPolicySaysSo()
    {
        var h = await SeedAsync(payloadKg: 10000, orderWeight: 12500, capacitySeverity: "Blocking");
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.CapacityExceeded);
        Assert.True(conflict.Blocking);
    }

    [Fact]
    public async Task VehicleWithoutConfiguredCapacity_SkipsTheCheckEntirely()
    {
        var h = await SeedAsync(payloadKg: null, volumeM3: null, orderWeight: 999999);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code is PlanningConflictCode.CapacityExceeded or PlanningConflictCode.CapacityCheckIncomplete);
    }

    [Fact]
    public async Task OrderWithoutData_MakesTheCheckExplicitlyIncomplete_WithoutInventingValues()
    {
        var h = await SeedAsync(orderWeight: null, orderVolume: null);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var info = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.CapacityCheckIncomplete);
        Assert.False(info.Blocking);
        Assert.Equal(TransportationService.Api.Common.Scheduling.ConflictSeverity.Information, info.Severity);
        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.CapacityExceeded);
    }

    [Fact]
    public async Task CargoLines_IncludingManualVolume_WinOverOrderTotals_AndTrailerCapacityWins()
    {
        // Order-level totals say 1 kg / 1 m³, but the structured cargo lines say much more —
        // the lines win. A trailer is attached, so ITS capacity is the measure.
        var h = await SeedAsync(payloadKg: 50000, volumeM3: 500, orderWeight: 1, orderVolume: 1);
        using var _ = h.Db;
        var trailerId = Guid.NewGuid();
        h.Db.Context.Trailers.Add(new Trailer
        {
            Id = trailerId, TenantId = h.TenantId, InternalNumber = "OPL-1", LicensePlate = "O-1",
            OperationalStatus = TrailerOperationalStatus.Available, IsActive = true,
            CapacityKg = 5000, VolumeM3 = 10,
        });
        h.Db.Context.CargoItems.AddRange(
            new CargoItem
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1,
                Description = "Paletten", ExpectedQuantity = 4, WeightPerUnitKg = 1200,
                VolumeM3 = 2m, VolumeIsManual = true, UnitType = PackageUnitType.EuroPallet,
            },
            new CargoItem
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 2,
                Description = "Kratten", ExpectedQuantity = 2, TotalWeightKg = 900, VolumeM3 = 1.5m,
            });
        await h.Db.Context.SaveChangesAsync();

        var trip = TripFor(h);
        trip.TrailerId = trailerId;
        var conflicts = await h.Sut.EvaluateAsync(trip, CancellationToken.None);

        // Weight: 4×1200 + 900 = 5700 > 5000; volume: 4×2 + 2×1.5 = 11 > 10 — both against the trailer.
        var capacity = conflicts.Where(c => c.Code == PlanningConflictCode.CapacityExceeded).ToList();
        Assert.Equal(2, capacity.Count);
        Assert.All(capacity, c => Assert.Contains("OPL-1", c.Description));
        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.CapacityCheckIncomplete);
    }
}
