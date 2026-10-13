using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

public class PackageGenerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, PackageGenerationService Sut, Guid TenantId, Guid OrderId, Guid LoadStopId, Guid UnloadStopId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var loadStopId = Guid.NewGuid();
        var unloadStopId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 22), Status = TransportOrderStatus.Confirmed,
            Stops =
            [
                new TransportOrderStop { Id = loadStopId, TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                new TransportOrderStop { Id = unloadStopId, TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, City = "Rotterdam" },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var sut = new PackageGenerationService(db.Context, tenant,
            new AuditService(db.Context, tenant, user),
            new PackageBarcodeService(db.Context, tenant, user, clock),
            new PackageEventWriter(db.Context, tenant, user, clock));
        return new Harness(db, sut, tenantId, orderId, loadStopId, unloadStopId);
    }

    private static CargoItem CargoLine(Harness h, int sequence, decimal quantity, string description = "Europalletten") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = h.TenantId,
        TransportOrderId = h.OrderId,
        Sequence = sequence,
        Description = description,
        ExpectedQuantity = quantity,
        UnitType = PackageUnitType.EuroPallet,
        WeightPerUnitKg = 800,
        LengthMeters = 1.2m,
        WidthMeters = 0.8m,
        HeightMeters = 1.5m,
        VolumeM3 = 1.44m,
        LoadingStopId = h.LoadStopId,
        UnloadingStopId = h.UnloadStopId,
        Reference = $"LIJN-{sequence}",
    };

    [Fact]
    public async Task Generate_CreatesOnePackagePerWholeUnit_CarryingCargoAttributes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var lineA = CargoLine(h, 1, 3);
        var lineB = CargoLine(h, 2, 2.5m, "Kratten");
        h.Db.Context.CargoItems.AddRange(lineA, lineB);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(6, result!.Created); // 3 + ceil(2.5)
        Assert.Equal(0, result.Cancelled);

        var packages = h.Db.Context.Packages.Where(p => p.TransportOrderId == h.OrderId).ToList();
        Assert.Equal(6, packages.Count);
        Assert.All(packages, p => Assert.StartsWith("PKG-", p.PackageNumber));
        Assert.Equal(6, packages.Select(p => p.PackageNumber).Distinct().Count());

        var fromLineA = packages.Where(p => p.CargoItemId == lineA.Id).ToList();
        Assert.Equal(3, fromLineA.Count);
        Assert.All(fromLineA, p =>
        {
            Assert.Equal(PackageUnitType.EuroPallet, p.UnitType);
            Assert.Equal(800, p.WeightKg);
            Assert.Equal(1.44m, p.VolumeM3);
            Assert.Equal(h.LoadStopId, p.LoadingStopId);
            Assert.Equal(h.UnloadStopId, p.DeliveryStopId);
            Assert.Equal("LIJN-1", p.CustomerReference);
            Assert.False(string.IsNullOrEmpty(p.BarcodeValue));
        });

        Assert.Equal(6, h.Db.Context.PackageEvents.Count(e => e.EventType == PackageEventType.Created));
    }

    [Fact]
    public async Task Generate_IsIdempotent_AndTopsUpAfterQuantityIncrease()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var line = CargoLine(h, 1, 3);
        h.Db.Context.CargoItems.Add(line);
        await h.Db.Context.SaveChangesAsync();

        var first = await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);
        Assert.Equal(3, first!.Created);

        // Retry: nothing new.
        var retry = await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);
        Assert.Equal(0, retry!.Created);
        Assert.Equal(3, retry.Unchanged);

        // Quantity 3 → 5: only the shortfall is generated.
        var tracked = await h.Db.Context.CargoItems.FirstAsync(c => c.Id == line.Id);
        tracked.ExpectedQuantity = 5;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var topUp = await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);
        Assert.Equal(2, topUp!.Created);
        Assert.Equal(5, h.Db.Context.Packages.Count(p => p.CargoItemId == line.Id
            && p.CurrentLifecycleStatus != PackageLifecycleStatus.Cancelled));
    }

    [Fact]
    public async Task Generate_QuantityDrop_CancelsOnlyUnscannedSurplus_AndReportsScannedRest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var line = CargoLine(h, 1, 3);
        h.Db.Context.CargoItems.Add(line);
        await h.Db.Context.SaveChangesAsync();
        await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);

        // Two of the three packages already have scan history (Loaded).
        var generated = h.Db.Context.Packages.Where(p => p.CargoItemId == line.Id)
            .OrderBy(p => p.PackageNumber).ToList();
        generated[0].CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
        generated[1].CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
        await h.Db.Context.SaveChangesAsync();

        var tracked = await h.Db.Context.CargoItems.FirstAsync(c => c.Id == line.Id);
        tracked.ExpectedQuantity = 1;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);

        // Surplus 2: only the untouched Created package is cancelled; the scanned one is
        // reported for the disposition flow — never silently removed.
        Assert.Equal(1, result!.Cancelled);
        Assert.Equal(1, result.RequiresAttention);
        Assert.NotNull(result.Message);
        Assert.Equal(2, h.Db.Context.Packages.Count(p => p.CargoItemId == line.Id
            && p.CurrentLifecycleStatus == PackageLifecycleStatus.Loaded));
        Assert.Equal(1, h.Db.Context.Packages.Count(p => p.CargoItemId == line.Id
            && p.CurrentLifecycleStatus == PackageLifecycleStatus.Cancelled));
    }

    [Fact]
    public async Task Generate_WithoutCargoOrUnknownOrder_IsSafe()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noCargo = await h.Sut.GenerateForOrderAsync(h.OrderId, CancellationToken.None);
        Assert.Equal(0, noCargo!.Created);
        Assert.NotNull(noCargo.Message);

        Assert.Null(await h.Sut.GenerateForOrderAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
