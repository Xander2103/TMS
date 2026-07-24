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
/// Coherent pricing architecture: deterministic precedence with explicit priority and
/// blocking ambiguity, billable quantity, agreements (minimum/surcharges/components),
/// order-measure bases and no-tariff diagnostics.
/// </summary>
public class PricingEngineV2Tests
{
    private static readonly DateOnly Today = new(2026, 7, 25);

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

    private static PriceCalculationRequest Request(
        Harness h, decimal quantity, Guid? customerId = null,
        IReadOnlyList<PriceCalculationLineDetail>? details = null,
        decimal? weightKg = null, decimal? distanceKm = null, int? palletCount = null,
        DateOnly? date = null) =>
        new(customerId ?? h.CustomerAId, date ?? Today,
            [new PriceCalculationLineInput(h.PalletUnitId, quantity, details)],
            "BE", null, weightKg, distanceKm, palletCount, []);

    private static SavePriceRuleRequest PerUnitRule(
        Harness h, Guid? customerId, decimal unitPrice, decimal? minimum = null,
        string name = "Pallets", int priority = 0,
        DateOnly? from = null, DateOnly? until = null,
        decimal? oversizeLengthCm = null, decimal? oversizeWidthCm = null, decimal? oversizeFactor = null,
        Guid? agreementId = null, decimal? baseAmount = null) =>
        new(customerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            name, from ?? Today.AddMonths(-1), until, true, unitPrice, minimum, null,
            AgreementId: agreementId, Priority: priority, BaseAmount: baseAmount,
            OversizeLengthCm: oversizeLengthCm, OversizeWidthCm: oversizeWidthCm, OversizeBillableFactor: oversizeFactor);

    [Fact]
    public async Task PerUnit_WithMinimum_PricesSpecExample()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // €22 per pallet, minimum €60 (spec §7.2).
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 22m, minimum: 60m), CancellationToken.None);

        Assert.Equal(88m, (await h.Engine.CalculateAsync(Request(h, 4), CancellationToken.None)).Total);
        Assert.Equal(60m, (await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task CustomersGetDifferentPrices_ForIdenticalShipment()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 30m, name: "Pallets A"), CancellationToken.None);
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerBId, 35m, name: "Pallets B"), CancellationToken.None);
        await h.Admin.CreateRuleAsync(PerUnitRule(h, null, 40m, name: "Standaard"), CancellationToken.None);

        Assert.Equal(90m, (await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None)).Total);
        Assert.Equal(105m, (await h.Engine.CalculateAsync(Request(h, 3, h.CustomerBId), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task Priority_BreaksSpecificityTies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 30m, name: "Laag", priority: 0), CancellationToken.None);
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 25m, name: "Hoog", priority: 10), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None);
        Assert.Equal(50m, result.Total);
        Assert.Contains(result.Lines, l => l.RuleName == "Hoog");
        Assert.Null(result.ConfigurationError);
    }

    [Fact]
    public async Task ExactTie_IsABlockingConfigurationError_NamingBothRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 30m, name: "Regel X"), CancellationToken.None);
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 25m, name: "Regel Y"), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Regel X", result.ConfigurationError);
        Assert.Contains("Regel Y", result.ConfigurationError);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task Oversize_OneActualPallet_BillsTwoPalletPlaces()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Contract: above 125×85 a pallet counts as two pallet places.
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 45m,
            oversizeLengthCm: 125m, oversizeWidthCm: 85m, oversizeFactor: 2m), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 1,
            details: [new PriceCalculationLineDetail(1, 160m, 120m)]), CancellationToken.None);

        var line = Assert.Single(result.Lines, l => l.RuleName is not null);
        Assert.Equal(1m, line.ActualQuantity);       // the physical order still has ONE pallet
        Assert.Equal(2m, line.BillableQuantity);     // the commercial calculation uses two places
        Assert.Equal(90m, result.Total);

        // A normal pallet keeps billing 1:1.
        var normal = await h.Engine.CalculateAsync(Request(h, 1,
            details: [new PriceCalculationLineDetail(1, 120m, 80m)]), CancellationToken.None);
        Assert.Equal(45m, normal.Total);
    }

    [Fact]
    public async Task Agreement_ComponentModel_MinimumAndSurcharges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerAId, "Distributie 2026", Today.AddMonths(-1), null, true, 200m, null,
            [new SavePricingAgreementSurchargeRequest("Duurtoeslag", SurchargeKind.Percent, 10m)]), CancellationToken.None);
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 22m, agreementId: agreement.Id), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None);

        // 3 × 22 = 66 → minimum 200 → +10% = 220.
        Assert.Contains(result.Lines, l => l.Label.StartsWith("Minimumtarief") && l.Amount == 134m);
        Assert.Contains(result.Lines, l => l.Label == "Duurtoeslag" && l.Amount == 20m);
        Assert.Equal(220m, result.Total);
        Assert.Contains(result.Lines, l => l.AgreementName == "Distributie 2026");
    }

    [Fact]
    public async Task ConvertedRateCardShape_PricesFromOrderMeasures()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // The shape a converted legacy rate card takes: agreement + order-level components.
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerAId, "Tarievenkaart 2026", Today.AddMonths(-1), null, true, null, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, null, PriceRuleBasis.Fixed, null, "Basisbedrag",
            Today.AddMonths(-1), null, true, 50m, null, null, AgreementId: agreement.Id), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, null, PriceRuleBasis.PerPallet, null, "Palletprijs",
            Today.AddMonths(-1), null, true, 8m, null, null, AgreementId: agreement.Id), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, null, PriceRuleBasis.PerTon, null, "Tonprijs",
            Today.AddMonths(-1), null, true, 12m, null, null, AgreementId: agreement.Id), CancellationToken.None);

        // No unit rules exist → the agreement's order-level components price the order.
        var result = await h.Engine.CalculateAsync(
            Request(h, 3, weightKg: 1500m, palletCount: 3), CancellationToken.None);

        // 50 + 3×8 + 1.5×12 = 92.
        Assert.Equal(92m, result.Total);
        Assert.False(result.RequiresManualPrice);
        Assert.Contains(result.Lines, l => l.Label == "Basisbedrag" && l.Amount == 50m);
        Assert.Contains(result.Lines, l => l.Label.StartsWith("Palletprijs") && l.Amount == 24m);
        Assert.Contains(result.Lines, l => l.Label.StartsWith("Tonprijs") && l.Amount == 18m);
    }

    [Fact]
    public async Task StandalonePerKm_WithBaseAmount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, null, PriceRuleBasis.PerKm, null, "Kilometertarief",
            Today.AddMonths(-1), null, true, 1.2m, null, null, BaseAmount: 25m), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3, distanceKm: 100m), CancellationToken.None);

        // 25 + 100 × 1.2 = 145; the unmatched unit line is replaced by the order tariff.
        Assert.Equal(145m, result.Total);
        Assert.False(result.RequiresManualPrice);

        // Without a known distance the rule is skipped with an explanation and pricing is manual.
        var noDistance = await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None);
        Assert.True(noDistance.RequiresManualPrice);
        Assert.Contains(noDistance.Lines, l => l.Label.Contains("overgeslagen"));
    }

    [Fact]
    public async Task EffectiveWindows_VersionPricesByTariffDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Version 1 until the end of September, version 2 (+4%) from 1 October (spec §12/15).
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 30m, name: "Distributie 2026",
            from: new DateOnly(2026, 1, 1), until: new DateOnly(2026, 9, 30)), CancellationToken.None);
        await h.Admin.CreateRuleAsync(PerUnitRule(h, h.CustomerAId, 31.20m, name: "Distributie 2026 (+4%)",
            from: new DateOnly(2026, 10, 1)), CancellationToken.None);

        var before = await h.Engine.CalculateAsync(Request(h, 3, date: new DateOnly(2026, 9, 15)), CancellationToken.None);
        var after = await h.Engine.CalculateAsync(Request(h, 3, date: new DateOnly(2026, 10, 15)), CancellationToken.None);

        Assert.Equal(90m, before.Total);
        Assert.Equal(93.60m, after.Total);
        Assert.Contains(after.Lines, l => l.RuleName == "Distributie 2026 (+4%)");
    }

    [Fact]
    public async Task NoTariff_NeverSilentZero_WithDiagnostics()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Engine.CalculateAsync(Request(h, 3, weightKg: 1350m), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.Contains(result.Lines, l => l.Label == "Geen geldig tarief gevonden voor deze order.");
        Assert.NotNull(result.Diagnostics);
        Assert.Contains(result.Diagnostics!, d => d.StartsWith("Klant: Klant A"));
        Assert.Contains(result.Diagnostics!, d => d.Contains("Tariefdatum"));
        Assert.Contains(result.Diagnostics!, d => d.Contains("3 × Europallet"));
        Assert.Contains(result.Diagnostics!, d => d.Contains("1350"));
        Assert.Equal(Today, result.TariffDate);
    }
}
