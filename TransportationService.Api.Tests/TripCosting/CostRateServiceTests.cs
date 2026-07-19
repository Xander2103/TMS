using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.TripCosting;

public class CostRateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(SqliteTestDbContext Db, CostRateService Sut, Guid TenantId)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var sut = new CostRateService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return (db, sut, tenantId);
    }

    private static SaveCostRateSetRequest Request(DateOnly effectiveFrom, decimal fuel = 1.5m) => new(
        effectiveFrom, "Test", fuel, 25m, 0.5m, 5m, 25m, 1.2m, 0.1m, 40m, 15m, 20m, 25m, 480, 1.5m, 30m, 2.68m, 2.31m);

    [Fact]
    public async Task Create_List_ResolveByDate()
    {
        var (db, sut, _) = await SeedAsync();
        using var _1 = db;

        var (jan, janError) = await sut.CreateAsync(Request(new DateOnly(2026, 1, 1), 1.5m), CancellationToken.None);
        var (jul, julError) = await sut.CreateAsync(Request(new DateOnly(2026, 7, 1), 1.8m), CancellationToken.None);
        Assert.Null(janError);
        Assert.Null(julError);
        Assert.NotNull(jan);
        Assert.NotNull(jul);

        var list = await sut.ListAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Equal(new DateOnly(2026, 7, 1), list[0].EffectiveFrom); // newest first

        Assert.Equal(1.5m, (await sut.GetForDateAsync(new DateOnly(2026, 6, 30), CancellationToken.None))!.FuelPricePerLitre);
        Assert.Equal(1.8m, (await sut.GetForDateAsync(new DateOnly(2026, 7, 1), CancellationToken.None))!.FuelPricePerLitre);
        Assert.Null(await sut.GetForDateAsync(new DateOnly(2025, 12, 31), CancellationToken.None));
    }

    [Fact]
    public async Task Create_RejectsDuplicateEffectiveDate_AndNegativeRates()
    {
        var (db, sut, _) = await SeedAsync();
        using var _1 = db;
        await sut.CreateAsync(Request(new DateOnly(2026, 1, 1)), CancellationToken.None);

        var (_, duplicateError) = await sut.CreateAsync(Request(new DateOnly(2026, 1, 1)), CancellationToken.None);
        Assert.NotNull(duplicateError);

        var (_, negativeError) = await sut.CreateAsync(Request(new DateOnly(2026, 2, 1), fuel: -1m), CancellationToken.None);
        Assert.NotNull(negativeError);
    }

    [Fact]
    public async Task RateCards_AreTenantIsolated()
    {
        var (db, sut, _) = await SeedAsync();
        using var _1 = db;
        await sut.CreateAsync(Request(new DateOnly(2026, 1, 1)), CancellationToken.None);

        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreign = new CostRateService(db.Context, foreignTenant,
            new AuditService(db.Context, foreignTenant, new DevCurrentUserContext(null)));

        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        Assert.Null(await foreign.GetForDateAsync(new DateOnly(2026, 6, 1), CancellationToken.None));
    }
}
