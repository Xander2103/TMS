using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>
/// Row-level customer overrides ("klantafwijkingen", corrections wave 2026-07-27 §2.2): one
/// deviating bracket-row price per customer without copying the whole shared table. Non-
/// overridden rows keep falling back to the shared bracket; ambiguity blocks, never guesses.
/// </summary>
public class BracketOverrideTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerAId, Guid CustomerBId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerAId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerBId, TenantId = tenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerAId, customerBId, palletUnitId);
    }

    /// <summary>Shared 1/2/3/4+ pallet table: €50 / €80 / €105 / €125 (spec example).</summary>
    private static SavePriceRuleRequest SharedBracketRule(Harness h, string name = "Palletstaffel") =>
        new(null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            name, Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null),
                new SavePriceRuleBracketRequest(2, 2, 80m, null),
                new SavePriceRuleBracketRequest(3, 3, 105m, null),
                new SavePriceRuleBracketRequest(4, null, 125m, null),
            ]);

    private static PriceCalculationRequest Request(Harness h, decimal quantity, Guid? customerId = null, DateOnly? date = null) =>
        new(customerId ?? h.CustomerAId, date ?? Today,
            [new PriceCalculationLineInput(h.PalletUnitId, quantity)], "BE", null, null, null, null, []);

    private static SavePriceRuleBracketOverrideRequest Row3For99(Harness h) =>
        new(h.CustomerAId, 3, 3, Price: 99m);

    [Fact]
    public async Task Override_ReplacesOnlyItsRow_AllOthersFallBackToShared()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        var created = await h.Admin.CreateBracketOverrideAsync(rule.Id, Row3For99(h), CancellationToken.None);
        Assert.NotNull(created);
        Assert.False(created.Orphaned);

        Assert.Equal(50m, (await h.Engine.CalculateAsync(Request(h, 1), CancellationToken.None)).Total);
        Assert.Equal(80m, (await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None)).Total);
        var three = await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None);
        Assert.Equal(99m, three.Total);
        Assert.Contains(three.Lines, l => l.Source.Contains("klantafwijking"));
        Assert.Equal(125m, (await h.Engine.CalculateAsync(Request(h, 5), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task Override_OnlyAppliesToItsOwnCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(rule.Id, Row3For99(h), CancellationToken.None);

        Assert.Equal(99m, (await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None)).Total);
        var b = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerBId), CancellationToken.None);
        Assert.Equal(105m, b.Total);
        Assert.DoesNotContain(b.Lines, l => l.Source.Contains("klantafwijking"));
    }

    [Fact]
    public async Task Override_RespectsEffectiveWindow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(rule.Id, new SavePriceRuleBracketOverrideRequest(
            h.CustomerAId, 3, 3, Price: 99m,
            EffectiveFrom: Today.AddDays(10), EffectiveUntil: Today.AddDays(20)), CancellationToken.None);

        Assert.Equal(105m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);
        Assert.Equal(99m, (await h.Engine.CalculateAsync(Request(h, 3, date: Today.AddDays(15)), CancellationToken.None)).Total);
        Assert.Equal(105m, (await h.Engine.CalculateAsync(Request(h, 3, date: Today.AddDays(25)), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task RemovingOverride_RestoresTheInheritedPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        var created = await h.Admin.CreateBracketOverrideAsync(rule.Id, Row3For99(h), CancellationToken.None);
        Assert.Equal(99m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);

        Assert.True(await h.Admin.DeleteBracketOverrideAsync(created!.Id, CancellationToken.None));
        Assert.Equal(105m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task OpenEndedRowOverride_KeepsSharedPricePerExtraUnit_UnlessProvided()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Open staffel", Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 3, 60m, null),
                new SavePriceRuleBracketRequest(4, null, 100m, 10m),
            ]), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(rule.Id, new SavePriceRuleBracketOverrideRequest(
            h.CustomerAId, 4, null, Price: 90m), CancellationToken.None);

        // 6 pallets: overridden base 90 + shared €10 × (6-4) = 110.
        Assert.Equal(110m, (await h.Engine.CalculateAsync(Request(h, 6), CancellationToken.None)).Total);

        // Providing the extra price in the override replaces it too: 90 + 5 × 2 = 100.
        var overrides = await h.Admin.ListBracketOverridesAsync(rule.Id, null, CancellationToken.None);
        await h.Admin.UpdateBracketOverrideAsync(overrides![0].Id, new SavePriceRuleBracketOverrideRequest(
            h.CustomerAId, 4, null, Price: 90m, PricePerExtraUnit: 5m), CancellationToken.None);
        Assert.Equal(100m, (await h.Engine.CalculateAsync(Request(h, 6), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task WeightBracketOverride_AppliesOnOrderLevelRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Order-level weight table (no unit): 0–500 kg €40, 501+ kg €70.
        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, null, PriceRuleBasis.WeightBracket, null,
            "Gewichtstaffel", Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(0, 500, 40m, null),
                new SavePriceRuleBracketRequest(501, null, 70m, null),
            ]), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(rule.Id, new SavePriceRuleBracketOverrideRequest(
            h.CustomerAId, 0, 500, Price: 35m), CancellationToken.None);

        var light = await h.Engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerAId, Today, [], "BE", null, 300m, null, null, []), CancellationToken.None);
        Assert.Equal(35m, light.Total);
        Assert.Contains(light.Lines, l => l.Label.Contains("klantafwijking"));

        var heavy = await h.Engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerAId, Today, [], "BE", null, 800m, null, null, []), CancellationToken.None);
        Assert.Equal(70m, heavy.Total);
    }

    [Fact]
    public async Task ConflictingOverrides_AreABlockingConfigurationError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        // The service refuses overlapping saves, so simulate pre-existing conflicting data.
        foreach (var price in new[] { 99m, 95m })
        {
            h.Db.Context.PriceRuleBracketOverrides.Add(new PriceRuleBracketOverride
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PriceRuleId = rule.Id,
                CustomerId = h.CustomerAId, FromQuantity = 3, ToQuantity = 3, Price = price,
            });
        }
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None);
        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Conflicterende klantafwijkingen", result.ConfigurationError);
    }

    [Fact]
    public async Task PrivateCustomerRule_StillOutranksSharedRule_OverrideIrrelevant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var shared = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(shared.Id, Row3For99(h), CancellationToken.None);
        // Customer A's own full bracket rule wins outright (tier 2 beats tier 0).
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Eigen staffel", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(1, null, 200m, null)]), CancellationToken.None);

        Assert.Equal(200m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task RuleRowEdit_OrphansTheOverride_AndStopsApplyingIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(rule.Id, Row3For99(h), CancellationToken.None);

        // Replace row 3 (3–3) by 3–4: the override's row identity no longer exists.
        await h.Admin.UpdateRuleAsync(rule.Id, SharedBracketRule(h) with
        {
            Brackets =
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null),
                new SavePriceRuleBracketRequest(2, 2, 80m, null),
                new SavePriceRuleBracketRequest(3, 4, 105m, null),
                new SavePriceRuleBracketRequest(5, null, 125m, null),
            ],
        }, CancellationToken.None);

        Assert.Equal(105m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);
        var overrides = await h.Admin.ListBracketOverridesAsync(rule.Id, null, CancellationToken.None);
        Assert.True(Assert.Single(overrides!).Orphaned);
    }

    [Fact]
    public async Task Validation_RejectsBadOverrides()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var shared = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);

        // Row identity must exist on the rule.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateBracketOverrideAsync(
            shared.Id, new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 3, 7, Price: 99m), CancellationToken.None));

        // Negative price refused.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateBracketOverrideAsync(
            shared.Id, new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 3, 3, Price: -1m), CancellationToken.None));

        // Foreign customer refused.
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Admin.CreateBracketOverrideAsync(
            shared.Id, new SavePriceRuleBracketOverrideRequest(Guid.NewGuid(), 3, 3, Price: 99m), CancellationToken.None));

        // Overlapping duplicate for the same row refused.
        await h.Admin.CreateBracketOverrideAsync(shared.Id, Row3For99(h), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateBracketOverrideAsync(
            shared.Id, new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 3, 3, Price: 90m), CancellationToken.None));

        // Customer-private rules take no overrides.
        var privateRule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Eigen staffel", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(1, null, 200m, null)]), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateBracketOverrideAsync(
            privateRule.Id, new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 1, null, Price: 150m), CancellationToken.None));

        // Non-bracket bases take no overrides.
        var perUnit = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Per pallet", Today.AddMonths(-1), null, true, 20m, null, null), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateBracketOverrideAsync(
            perUnit.Id, new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 1, null, Price: 15m), CancellationToken.None));
    }

    [Fact]
    public async Task Overrides_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(SharedBracketRule(h), CancellationToken.None);

        // A foreign tenant's override row for the same rule/customer ids must never apply here.
        h.Db.Context.PriceRuleBracketOverrides.Add(new PriceRuleBracketOverride
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), PriceRuleId = rule.Id,
            CustomerId = h.CustomerAId, FromQuantity = 3, ToQuantity = 3, Price = 1m,
        });
        await h.Db.Context.SaveChangesAsync();

        Assert.Equal(105m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);
        var listed = await h.Admin.ListBracketOverridesAsync(rule.Id, null, CancellationToken.None);
        Assert.Empty(listed!);
    }
}
