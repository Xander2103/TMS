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
/// Phase 3: multidimensional bracket caps (weight/volume/loading-meters tightness), per-next-unit
/// progressive bracket pricing, and a per-rule maximum charge applied after the minimum floor.
/// </summary>
public class BracketDimensionTests
{
    private static readonly DateOnly Today = new(2026, 7, 26);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant X", CustomerNumber = "KL-1", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerId, palletUnitId);
    }

    /// <summary>An order-level request (no matching unit rule) — used for WeightBracket/PerKm order tariffs.</summary>
    private static PriceCalculationRequest OrderRequest(
        Harness h, decimal? weightKg = null, decimal? volumeM3 = null, decimal? loadingMeters = null,
        decimal? distanceKm = null) =>
        new(h.CustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, 1)],
            "BE", null, weightKg, distanceKm, null, [],
            VolumeM3: volumeM3, LoadingMeters: loadingMeters);

    private static PriceCalculationRequest UnitRequest(Harness h, decimal quantity, decimal? weightKg = null) =>
        new(h.CustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, quantity)],
            "BE", null, weightKg, null, null, []);

    // --- 1. Carrier table: weight + loading-meters caps on an order-level WeightBracket rule ---

    [Fact]
    public async Task CarrierTable_PicksTightestRowWhoseCapsHold()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.WeightBracket, null, "Vervoerderstabel",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(0, null, 30m, null, WeightToKg: 100m, LoadingMetersTo: 0.2m),
                new SavePriceRuleBracketRequest(0, null, 48m, null, WeightToKg: 500m, LoadingMetersTo: 0.5m),
                new SavePriceRuleBracketRequest(0, null, 72m, null, WeightToKg: 1000m, LoadingMetersTo: 1.0m),
            ]), CancellationToken.None);

        var tight = await h.Engine.CalculateAsync(OrderRequest(h, weightKg: 350m, loadingMeters: 0.4m), CancellationToken.None);
        Assert.Equal(48m, tight.Total);
        Assert.False(tight.RequiresManualPrice);

        var loose = await h.Engine.CalculateAsync(OrderRequest(h, weightKg: 90m, loadingMeters: 0.15m), CancellationToken.None);
        Assert.Equal(30m, loose.Total);

        var tooHeavy = await h.Engine.CalculateAsync(OrderRequest(h, weightKg: 1200m, loadingMeters: 0.4m), CancellationToken.None);
        Assert.True(tooHeavy.RequiresManualPrice);
        Assert.Contains(tooHeavy.Lines, l => l.Label.Contains("geen gewicht of staffel"));
    }

    // --- 2. Quantity + weight cap combined on a unit-line QuantityBracket rule ---

    [Fact]
    public async Task QuantityAndWeightCap_CaplessRowWinsWhenWeightUnknown()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null, "Pallets met gewichtscap",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 2, 40m, null, WeightToKg: 500m),
                new SavePriceRuleBracketRequest(1, 2, 55m, null),
            ]), CancellationToken.None);

        var light = await h.Engine.CalculateAsync(UnitRequest(h, 2, weightKg: 300m), CancellationToken.None);
        Assert.Equal(40m, light.Total);

        var heavy = await h.Engine.CalculateAsync(UnitRequest(h, 2, weightKg: 800m), CancellationToken.None);
        Assert.Equal(55m, heavy.Total);

        var unknownWeight = await h.Engine.CalculateAsync(UnitRequest(h, 2), CancellationToken.None);
        Assert.Equal(55m, unknownWeight.Total);
    }

    // --- 3. PerNextUnit: sums the bracket price of each unit index ---

    [Fact]
    public async Task PerNextUnit_SumsBracketPricePerPiece()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null, "Per volgende eenheid",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 60m, null),
                new SavePriceRuleBracketRequest(2, 2, 55m, null),
                new SavePriceRuleBracketRequest(3, 3, 50m, null),
                new SavePriceRuleBracketRequest(4, null, 45m, null),
            ], BracketMode: BracketSelectionMode.PerNextUnit), CancellationToken.None);

        Assert.Equal(210m, (await h.Engine.CalculateAsync(UnitRequest(h, 4), CancellationToken.None)).Total);
        Assert.Equal(140m, (await h.Engine.CalculateAsync(UnitRequest(h, 2.5m), CancellationToken.None)).Total);
        Assert.Equal(300m, (await h.Engine.CalculateAsync(UnitRequest(h, 6), CancellationToken.None)).Total);
    }

    // --- 4. MaximumAmount caps the rule amount, applied AFTER the minimum floor ---

    [Fact]
    public async Task MaximumAmount_CapsAboveTheComputedAmount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null, "Kilometertarief met max",
            Today.AddMonths(-1), null, true, 1.5m, null, null, MaximumAmount: 500m), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(OrderRequest(h, distanceKm: 600m), CancellationToken.None);

        // 1.50 × 600 = 900 → capped at the maximum of 500.
        Assert.Equal(500m, result.Total);
    }

    [Fact]
    public async Task MaximumAmount_MinimumFloorStillAppliesBelowTheMaximum()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null, "Kilometertarief min+max",
            Today.AddMonths(-1), null, true, 1.5m, 150m, null, MaximumAmount: 500m), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(OrderRequest(h, distanceKm: 50m), CancellationToken.None);

        // 1.50 × 50 = 75 → raised to the minimum of 150; still well below the 500 maximum.
        Assert.Equal(150m, result.Total);
    }

    // --- 5. Validation ---

    [Fact]
    public async Task Validation_MinimumAboveMaximum_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null, "Min boven max",
            Today.AddMonths(-1), null, true, 10m, 100m, null, MaximumAmount: 50m), CancellationToken.None));
    }

    [Fact]
    public async Task Validation_PerNextUnit_WithGap_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null, "Gat in staffels",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 60m, null),
                new SavePriceRuleBracketRequest(3, 3, 50m, null),
            ], BracketMode: BracketSelectionMode.PerNextUnit), CancellationToken.None));
    }

    [Fact]
    public async Task Validation_PerNextUnit_OnWeightBracket_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.WeightBracket, null, "Verkeerde basis",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(0, 999, 60m, null),
                new SavePriceRuleBracketRequest(1000, null, 45m, null),
            ], BracketMode: BracketSelectionMode.PerNextUnit), CancellationToken.None));
    }

    [Fact]
    public async Task Validation_DifferentDimensionCaps_DoNotCountAsOverlap()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Same quantity band (0..∞), different WeightToKg caps: legitimately coexist (carrier table).
        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.WeightBracket, null, "Coexisting caps",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(0, null, 30m, null, WeightToKg: 100m),
                new SavePriceRuleBracketRequest(0, null, 48m, null, WeightToKg: 500m),
            ]), CancellationToken.None);

        Assert.Equal(2, rule.Brackets.Count);
    }

    // --- 6. Existing (cap-less) bracket behaviour is unchanged ---

    [Fact]
    public async Task CaplessBrackets_KeepExistingBehaviour()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null, "Klassieke staffel",
            Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null),
                new SavePriceRuleBracketRequest(2, null, 85m, 20m),
            ]), CancellationToken.None);

        Assert.Equal(50m, (await h.Engine.CalculateAsync(UnitRequest(h, 1), CancellationToken.None)).Total);
        // Open-ended bracket + extra-per-unit above FromQuantity: 85 + 1 × 20 = 105.
        Assert.Equal(105m, (await h.Engine.CalculateAsync(UnitRequest(h, 3), CancellationToken.None)).Total);
    }
}
