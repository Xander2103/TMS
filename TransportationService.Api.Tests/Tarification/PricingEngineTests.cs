using Microsoft.EntityFrameworkCore;
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

/// <summary>Spec 7: configurable unit pricing — brackets, zones, weight, hourly, fixed, options, fallback.</summary>
public class PricingEngineTests
{
    private static readonly DateOnly Today = new(2026, 7, 24);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid HourUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var hourUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant X", CustomerNumber = "KL-1", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = hourUnitId, TenantId = tenantId, Code = "UUR", Name = "Uur", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerId, palletUnitId, hourUnitId);
    }

    private static PriceCalculationRequest Request(
        Harness h, decimal quantity, Guid? unitId = null,
        string? postalCode = null, decimal? weightKg = null, IReadOnlyList<Guid>? options = null) =>
        new(h.CustomerId, Today, [new PriceCalculationLineInput(unitId ?? h.PalletUnitId, quantity)],
            "BE", postalCode, weightKg, null, null, options ?? []);

    [Fact]
    public async Task QuantityBrackets_PriceTheExampleTable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets klant X", Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50, null),
                new SavePriceRuleBracketRequest(2, 2, 85, null),
                new SavePriceRuleBracketRequest(3, null, 115, 25),
            ]), CancellationToken.None);

        Assert.Equal(50m, (await h.Engine.CalculateAsync(Request(h, 1), CancellationToken.None)).Total);
        Assert.Equal(85m, (await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None)).Total);
        Assert.Equal(115m, (await h.Engine.CalculateAsync(Request(h, 3), CancellationToken.None)).Total);
        // Open-ended bracket: 115 + 2 × 25.
        Assert.Equal(165m, (await h.Engine.CalculateAsync(Request(h, 5), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task ZoneRule_WinsOverZoneless_AndZoneIsExplained()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zone = await h.Admin.CreateZoneAsync(new SavePricingZoneRequest("Z3", "Zone 3", true, 0,
            [new SavePricingZoneAreaRequest("BE", "3000", "3999")]), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets standaard", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(1, null, 100, 30)]), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, zone.Id,
            "Pallets Z3", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 145, null)]), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3, postalCode: "3500"), CancellationToken.None);
        Assert.Equal(145m, result.Total);
        Assert.Equal("Z3", result.ZoneCode);
        Assert.Contains(result.Lines, l => l.Source == "Pallets Z3");

        // Outside the zone the zone-less rule prices.
        var outside = await h.Engine.CalculateAsync(Request(h, 3, postalCode: "2000"), CancellationToken.None)
;
        Assert.Equal(160m, outside.Total); // 100 + 2 × 30
    }

    [Fact]
    public async Task WeightHourlyAndFixed_Bases_Work()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var kgUnit = new UnitType { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "KG", Name = "Kilogram", IsActive = true };
        h.Db.Context.UnitTypes.Add(kgUnit);
        await h.Db.Context.SaveChangesAsync();

        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, kgUnit.Id, PriceRuleBasis.WeightBracket, null,
            "Gewicht", Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(0, 999, 60, null),
                new SavePriceRuleBracketRequest(1000, null, 120, null),
            ]), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.HourUnitId, PriceRuleBasis.Hourly, null,
            "Uurtarief", Today.AddMonths(-1), null, true, 75, null, null), CancellationToken.None);

        var weight = await h.Engine.CalculateAsync(Request(h, 1, kgUnit.Id, weightKg: 1500), CancellationToken.None);
        Assert.Equal(120m, weight.Total);

        var hourly = await h.Engine.CalculateAsync(Request(h, 3, h.HourUnitId), CancellationToken.None);
        Assert.Equal(225m, hourly.Total); // 3 uur × €75

        // Customer-specific fixed price without unit (flat per order).
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.Fixed, null,
            "Vaste ritprijs", Today.AddMonths(-1), null, true, 300, null, null), CancellationToken.None);
    }

    [Fact]
    public async Task CustomerRule_BeatsCompanyDefault_AndEffectiveWindowApplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Bedrijfsstandaard pallet", Today.AddMonths(-1), null, true, 40, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Klantprijs pallet", Today.AddMonths(-1), null, true, 35, null, null), CancellationToken.None);
        // Expired customer rule must not win.
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Verlopen promo", Today.AddYears(-2), Today.AddYears(-1), true, 1, null, null), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None);
        Assert.Equal(70m, result.Total); // 2 × 35 klantprijs
        Assert.Contains(result.Lines, l => l.Source == "Klantprijs pallet");

        // A different customer without own rules gets the company default.
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = h.TenantId, Name = "Klant Y", CustomerNumber = "KL-2", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        var other = await h.Engine.CalculateAsync(
            new PriceCalculationRequest(otherCustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, 2)], "BE", null, null, null, null, []),
            CancellationToken.None);
        Assert.Equal(80m, other.Total); // 2 × 40 bedrijfsstandaard
    }

    [Fact]
    public async Task ServiceOptions_CustomerPriceWinsOverDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallet", Today.AddMonths(-1), null, true, 50, null, null), CancellationToken.None);
        var voor8 = await h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("VOOR8", "Levering vóór 08:00", SurchargeKind.Fixed, 25, true, 0), CancellationToken.None);
        var brandstof = await h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("FUEL", "Brandstoftoeslag", SurchargeKind.Percent, 10, true, 1), CancellationToken.None);
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId, new SaveCustomerPricingConfigRequest(
            [], [new SaveCustomerOptionPriceRequest(brandstof.Id, 5)]), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(
            Request(h, 2, options: [voor8.Id, brandstof.Id]), CancellationToken.None);

        // 2×50 = 100 base + 25 fixed + 5% of 100.
        Assert.Equal(130m, result.Total);
        Assert.Contains(result.Lines, l => l.Label == "Levering vóór 08:00" && l.Amount == 25m && l.Source == "Standaardtarief");
        Assert.Contains(result.Lines, l => l.Label == "Brandstoftoeslag" && l.Amount == 5m && l.Source == "Klantprijs");
    }

    [Fact]
    public async Task DieselSurcharge_IsInformational_NotInTotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallet", Today.AddMonths(-1), null, true, 100, null, null), CancellationToken.None);
        h.Db.Context.Set<CustomerDieselSurcharge>().Add(new CustomerDieselSurcharge
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, Enabled = true, Percent = 12.5m,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Engine.CalculateAsync(Request(h, 1), CancellationToken.None);
        Assert.Equal(100m, result.Total);
        Assert.Equal(112.5m, result.TotalWithInformational);
        var diesel = Assert.Single(result.Lines, l => l.Informational);
        Assert.Equal(12.5m, diesel.Amount);
    }

    [Fact]
    public async Task LegacyRateCard_ConvertsOnce_AndKeepsPricing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Set<RateCard>().Add(new RateCard
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            Name = "Kaart 2026", EffectiveFrom = Today.AddMonths(-1), BaseAmount = 80,
            PerPalletRate = 8m, PerTonRate = 12m, MinimumAmount = 100m,
            Surcharges =
            [
                new RateSurcharge { Id = Guid.NewGuid(), TenantId = h.TenantId, Name = "ADR", Kind = SurchargeKind.Fixed, Value = 10m },
            ],
        });
        await h.Db.Context.SaveChangesAsync();

        await RateCardConversionService.ConvertAsync(h.Db.Context);
        // Idempotent: a second run converts nothing extra.
        await RateCardConversionService.ConvertAsync(h.Db.Context);
        Assert.Equal(1, await h.Db.Context.PricingAgreements.CountAsync(a => a.TenantId == h.TenantId));

        // Same order as the legacy quote: 80 base + 3×8 + 1.5×12 = 122 → +10 ADR = 132.
        var request = new PriceCalculationRequest(h.CustomerId, Today,
            [new PriceCalculationLineInput(h.PalletUnitId, 3)], "BE", null, 1500m, null, 3, []);
        var result = await h.Engine.CalculateAsync(request, CancellationToken.None);

        Assert.Equal(132m, result.Total);
        Assert.False(result.RequiresManualPrice);
        Assert.Contains(result.Lines, l => l.AgreementName == "Kaart 2026");
        Assert.Contains(result.Lines, l => l.Label == "ADR" && l.Amount == 10m);

        // The legacy card rows are preserved untouched.
        Assert.Equal(1, await h.Db.Context.RateCards.CountAsync(c => c.TenantId == h.TenantId));
    }

    [Fact]
    public async Task LegacyRateCard_Minimum_AppliesAfterConversion()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Set<RateCard>().Add(new RateCard
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            Name = "Kaart minimum", EffectiveFrom = Today.AddMonths(-1), BaseAmount = 30, MinimumAmount = 90m,
        });
        await h.Db.Context.SaveChangesAsync();
        await RateCardConversionService.ConvertAsync(h.Db.Context);

        var result = await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None);

        // 30 base → minimum 90 kicks in, explained as a separate line.
        Assert.Equal(90m, result.Total);
        Assert.Contains(result.Lines, l => l.Label.StartsWith("Minimumtarief"));
    }

    [Fact]
    public async Task NothingConfigured_RequiresManualPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var result = await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None);
        Assert.True(result.RequiresManualPrice);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task Rules_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // A rule owned by another tenant for the same unit id must never price this tenant.
        h.Db.Context.PriceRules.Add(new PriceRule
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CustomerId = null, UnitTypeId = h.PalletUnitId,
            Basis = PriceRuleBasis.PerUnit, Name = "Vreemde regel", EffectiveFrom = Today.AddMonths(-1),
            IsActive = true, UnitPrice = 1m,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Engine.CalculateAsync(Request(h, 2), CancellationToken.None);
        Assert.True(result.RequiresManualPrice);
    }

    [Fact]
    public async Task Admin_RejectsOverlappingBrackets_AndForeignReferences()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() =>
            h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
                h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
                "Overlap", Today, null, true, null, null,
                [
                    new SavePriceRuleBracketRequest(1, 5, 50, null),
                    new SavePriceRuleBracketRequest(3, 8, 80, null),
                ]), CancellationToken.None));

        await Assert.ThrowsAsync<TransportationService.Api.Common.InvalidTenantReferenceException>(() =>
            h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
                Guid.NewGuid(), h.PalletUnitId, PriceRuleBasis.PerUnit, null,
                "Vreemde klant", Today, null, true, 10, null, null), CancellationToken.None));
    }
}
