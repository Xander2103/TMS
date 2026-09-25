using TransportationService.Api.Common.Scheduling;
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

/// <summary>
/// D4: heaviest single unit vs. the vehicle's tail-lift capacity. Exceeding is a WARNING with its
/// own code; an unknown capacity or per-unit weight is "nog te controleren" — never "safe".
/// </summary>
public class TailLiftConflictTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, PlanningConflictService Sut, Guid TenantId, Guid VehicleId, Guid OrderId);

    private static async Task<Harness> SeedAsync(
        bool hasTailLift = true, decimal? tailLiftCapacityKg = 750, string capacitySeverity = "Blocking",
        decimal? orderWeight = null, params (decimal? PerUnit, decimal? Total)[] lines)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, CapacityConflictSeverity = capacitySeverity });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1",
            OperationalStatus = VehicleOperationalStatus.Available, IsActive = true,
            PayloadKg = 20000, HasTailLift = hasTailLift, TailLiftCapacityKg = tailLiftCapacityKg,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 9, 22), Status = TransportOrderStatus.Confirmed, WeightKg = orderWeight, VolumeM3 = 1,
        });
        var sequence = 1;
        foreach (var (perUnit, total) in lines)
        {
            db.Context.CargoItems.Add(new CargoItem
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, Sequence = sequence++,
                Description = "Kist", ExpectedQuantity = 2, WeightPerUnitKg = perUnit, TotalWeightKg = total, VolumeM3 = 0.5m,
            });
        }

        await db.Context.SaveChangesAsync();

        var sut = new PlanningConflictService(db.Context, new DevTenantContext(tenantId),
            new QualificationStatusCalculator(), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId, orderId);
    }

    private static Trip TripFor(Harness h) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1",
        TripDate = new DateOnly(2026, 9, 22), VehicleId = h.VehicleId, Status = TripStatus.Draft,
        Orders = [new TripOrder { TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1 }],
    };

    [Fact]
    public async Task HeaviestUnitAboveTheTailLiftCapacity_Warns_EvenWhenTheTenantBlocksOnCapacity()
    {
        var h = await SeedAsync(tailLiftCapacityKg: 750, lines: [(400m, 800m), (900m, 1800m)]);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.TailLiftCapacityExceeded);
        Assert.False(conflict.Blocking);
        Assert.Equal(ConflictSeverity.Warning, conflict.Severity);
        Assert.Equal(ConflictCategory.Capacity, conflict.Category);
        Assert.Equal("Vehicle", conflict.RelatedEntityType);
        Assert.Contains("900", conflict.Description);
        Assert.Contains("750", conflict.Description);
        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.CapacityCheckIncomplete);
    }

    [Fact]
    public async Task HeaviestUnitWithinTheCapacity_SaysNothing_NeverSafe()
    {
        var h = await SeedAsync(tailLiftCapacityKg: 1000, lines: [(400m, 800m), (900m, 1800m)]);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code is PlanningConflictCode.TailLiftCapacityExceeded or PlanningConflictCode.CapacityCheckIncomplete);
        Assert.DoesNotContain(conflicts, c => c.Description.Contains("veilig", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnknownTailLiftCapacity_IsStillToBeChecked()
    {
        var h = await SeedAsync(tailLiftCapacityKg: null, lines: [(900m, 1800m)]);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var info = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.CapacityCheckIncomplete);
        Assert.Equal(ConflictSeverity.Information, info.Severity);
        Assert.False(info.Blocking);
        Assert.StartsWith("Capaciteit nog te controleren", info.Description);
        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.TailLiftCapacityExceeded);
    }

    [Fact]
    public async Task UnknownPerUnitWeight_IsStillToBeChecked_AndNeverDerivedFromTheTotal()
    {
        // 2 units, 1800 kg in total: 900 kg each WOULD exceed 750 — but that is a guess, not data.
        var h = await SeedAsync(tailLiftCapacityKg: 750, lines: [(null, 1800m)]);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.TailLiftCapacityExceeded);
        var info = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.CapacityCheckIncomplete);
        Assert.StartsWith("Capaciteit nog te controleren", info.Description);
        Assert.Contains("gewicht per eenheid", info.Description);
    }

    [Fact]
    public async Task OrderWeightWithoutAnyGoodsLine_IsStillToBeChecked()
    {
        var h = await SeedAsync(tailLiftCapacityKg: 750, orderWeight: 5000);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.Single(conflicts, c => c.Code == PlanningConflictCode.CapacityCheckIncomplete);
    }

    [Fact]
    public async Task VehicleWithoutTailLift_IsNotChecked()
    {
        var h = await SeedAsync(hasTailLift: false, tailLiftCapacityKg: null, lines: [(900m, 1800m)]);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code is PlanningConflictCode.TailLiftCapacityExceeded or PlanningConflictCode.CapacityCheckIncomplete);
    }

    [Fact]
    public async Task AnotherTenantsCargo_IsNeverWeighed()
    {
        var h = await SeedAsync(tailLiftCapacityKg: 750, lines: [(100m, 200m)]);
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        // A foreign goods line pointing at OUR order id must not count.
        h.Db.Context.CargoItems.Add(new CargoItem
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, TransportOrderId = h.OrderId, Sequence = 9,
            Description = "Vreemd", ExpectedQuantity = 1, WeightPerUnitKg = 5000m, TotalWeightKg = 5000m,
        });
        await h.Db.Context.SaveChangesAsync();

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.TailLiftCapacityExceeded);
    }
}
