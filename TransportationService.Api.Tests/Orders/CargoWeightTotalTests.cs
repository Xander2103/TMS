using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// D4: total weight = quantity × weight per unit while no total was sent; a sent total is kept;
/// a per-unit weight is NEVER derived backwards from a total.
/// </summary>
public class CargoWeightTotalTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(SqliteTestDbContext Db, TransportOrderService Sut, Guid CustomerId)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1 });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TransportOrderService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return (db, sut, customerId);
    }

    private static CreateTransportOrderRequest Request(Guid customerId, params CargoItemInput[] cargo) => new(
        customerId, "PO-1", new DateOnly(2026, 9, 22), null, null, null, null, null, null, false, false, null, null,
        [
            new TransportOrderStopInput(StopType.Loading, null, null, null, null, "Antwerpen", "BE", null, null, null, null),
            new TransportOrderStopInput(StopType.Unloading, null, null, null, null, "Gent", "BE", null, null, null, null),
        ],
        CargoItems: cargo);

    [Fact]
    public async Task PerUnitWeightAndQuantity_WithoutTotal_ComputeTheTotal()
    {
        var (db, sut, customerId) = await SeedAsync();
        using var _ = db;

        var created = await sut.CreateAsync(
            Request(customerId, new CargoItemInput("Kisten", null, 4, "st", null, WeightPerUnitKg: 250.5m)), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var line = Assert.Single(created.Order!.CargoItems);
        Assert.Equal(250.5m, line.WeightPerUnitKg);
        Assert.Equal(1002m, line.TotalWeightKg);
        Assert.Equal(1002m, created.Order.WeightKg); // header summary follows the lines
    }

    [Fact]
    public async Task BothGiven_KeepsWhatWasSent()
    {
        var (db, sut, customerId) = await SeedAsync();
        using var _ = db;

        var created = await sut.CreateAsync(
            Request(customerId, new CargoItemInput("Kisten", null, 4, "st", null, TotalWeightKg: 1100m, WeightPerUnitKg: 250m)),
            CancellationToken.None);

        var line = Assert.Single(created.Order!.CargoItems);
        Assert.Equal(1100m, line.TotalWeightKg);
        Assert.Equal(250m, line.WeightPerUnitKg);
    }

    [Fact]
    public async Task TotalOnly_NeverDerivesAPerUnitWeight()
    {
        var (db, sut, customerId) = await SeedAsync();
        using var _ = db;

        var created = await sut.CreateAsync(
            Request(customerId, new CargoItemInput("Kisten", null, 4, "st", null, TotalWeightKg: 1000m)), CancellationToken.None);

        var line = Assert.Single(created.Order!.CargoItems);
        Assert.Equal(1000m, line.TotalWeightKg);
        Assert.Null(line.WeightPerUnitKg);
        Assert.Null((await db.Context.CargoItems.AsNoTracking().SingleAsync()).WeightPerUnitKg);
    }

    [Fact]
    public async Task Update_ComputesTheTotalForAnEditedLine_TheSameWay()
    {
        var (db, sut, customerId) = await SeedAsync();
        using var _ = db;
        var created = await sut.CreateAsync(
            Request(customerId, new CargoItemInput("Kisten", null, 4, "st", null, TotalWeightKg: 1000m)), CancellationToken.None);
        var d = created.Order!;

        var updated = await sut.UpdateAsync(d.Id, new UpdateTransportOrderRequest(
            d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
            d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
            d.AgreedPrice, d.Notes,
            d.Stops.Select(s => new TransportOrderStopInput(s.StopType, null, null, null, null, s.City, s.CountryCode, null, null, null, null, Id: s.Id)).ToList(),
            CargoItems: [new CargoItemInput("Kisten", null, 6, "st", null, WeightPerUnitKg: 100m, Id: d.CargoItems[0].Id)]),
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var line = Assert.Single(updated.Order!.CargoItems);
        Assert.Equal(600m, line.TotalWeightKg);
    }
}
