using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>Spec 7: order save prices via the engine, snapshots the breakdown, override is guarded.</summary>
public class OrderPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant, new RateCardService(db.Context, tenant, audit));
        var permissions = new PermissionSet();
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, permissions);
        return new Harness(db, sut, admin, permissions, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(
        Guid customerId, decimal quantity = 3, decimal? agreedPrice = null,
        IReadOnlyList<Guid>? serviceOptionIds = null, bool priceIsManual = false, string? overrideReason = null) => new(
        customerId, "REF-1", new DateOnly(2026, 7, 24), "Pallets", quantity, null, null, null, null, false, false,
        agreedPrice, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        QuantityUnitCode: "EUROPALLET", ServiceOptionIds: serviceOptionIds,
        PriceIsManual: priceIsManual, PriceOverrideReason: overrideReason);

    private static async Task SeedPalletBracketsAsync(Harness h)
    {
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets klant X", new DateOnly(2026, 1, 1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50, null),
                new SavePriceRuleBracketRequest(2, 2, 85, null),
                new SavePriceRuleBracketRequest(3, null, 115, 25),
            ]), CancellationToken.None);
    }

    [Fact]
    public async Task Create_CalculatesPrice_AndSnapshotsBreakdown()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(115m, created.Order!.CalculatedPrice);
        Assert.Equal(115m, created.Order.AgreedPrice);
        Assert.False(created.Order.PriceIsManual);
        Assert.NotNull(created.Order.PricingLines);
        Assert.Contains(created.Order.PricingLines!, l => l.Source == "Pallets klant X");
    }

    [Fact]
    public async Task ManualOverride_RequiresPermissionAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var noPermission = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true, overrideReason: "Afspraak telefonisch"),
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noPermission.Outcome);

        h.Permissions.Codes.Add("orders.override_price");
        var noReason = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noReason.Outcome);

        var allowed = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true, overrideReason: "Afspraak telefonisch"),
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, allowed.Outcome);
        Assert.Equal(99m, allowed.Order!.AgreedPrice);
        Assert.Equal(115m, allowed.Order.CalculatedPrice); // engine result stays visible next to the override
        Assert.True(allowed.Order.PriceIsManual);
        Assert.Equal("Afspraak telefonisch", allowed.Order.PriceOverrideReason);
    }

    [Fact]
    public async Task NoPricingConfig_LegacyManualEntry_StillWorks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, agreedPrice: 1450m), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(1450m, created.Order!.AgreedPrice);
        Assert.Null(created.Order.CalculatedPrice);
        Assert.False(created.Order.PriceIsManual);
    }

    [Fact]
    public async Task Snapshot_SurvivesLaterTariffChanges_UntilResave()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        Assert.Equal(115m, created.Order!.AgreedPrice);

        // Master data changes afterwards; the historical order must not move.
        var rule = await h.Db.Context.PriceRules.Include(r => r.Brackets).SingleAsync();
        foreach (var bracket in rule.Brackets)
        {
            bracket.Price += 1000m;
        }

        await h.Db.Context.SaveChangesAsync();

        var reloaded = await h.Sut.GetByIdAsync(created.Order.Id, CancellationToken.None);
        Assert.Equal(115m, reloaded!.AgreedPrice);
        Assert.Equal(115m, reloaded.CalculatedPrice);
        Assert.Contains(reloaded.PricingLines!, l => l.Amount == 115m);
    }

    [Fact]
    public async Task ServiceOptions_AreSnapshotted_AndBecomeSeparateInvoiceLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var voor8 = await h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("VOOR8", "Levering vóór 08:00", SurchargeKind.Fixed, 25, true, 0), CancellationToken.None);

        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, quantity: 3, serviceOptionIds: [voor8.Id]), CancellationToken.None);

        Assert.Equal(140m, created.Order!.AgreedPrice); // 115 + 25
        var serviceLine = Assert.Single(created.Order.ServiceLines!);
        Assert.Equal("Levering vóór 08:00", serviceLine.Name);
        Assert.Equal(25m, serviceLine.Amount);
    }
}
