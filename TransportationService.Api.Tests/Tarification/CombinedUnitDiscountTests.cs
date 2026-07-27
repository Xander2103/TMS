using TransportationService.Api.Data;
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
/// Pure helper math (spec §29-31): grouping merge, weighted equivalent count, tier lookup and the
/// eligible-base apportioning — all exercised directly, no database, so the arithmetic itself is
/// verified independently of the engine wiring (covered separately in <see cref="CombinedUnitDiscountTests"/>).
/// </summary>
public class CombinedUnitDiscountMathTests
{
    [Fact]
    public void MergeGroups_Order_SumsEveryGroupIntoOne()
    {
        var euro = Guid.NewGuid();
        var groups = new[]
        {
            new PriceCalculationGroup("stop1", "Antwerpen", [new PriceCalculationGroupUnit(euro, 3)], "antwerpen"),
            new PriceCalculationGroup("stop2", "Mechelen", [new PriceCalculationGroupUnit(euro, 2)], "mechelen"),
        };

        var merged = CombinedUnitDiscountMath.MergeGroups(groups, DegressionScope.Order);

        var only = Assert.Single(merged);
        Assert.Equal(5, only.Quantities[euro]);
    }

    [Fact]
    public void MergeGroups_Stop_KeepsGroupsSeparate_EvenWithSameAddressKey()
    {
        var euro = Guid.NewGuid();
        var groups = new[]
        {
            new PriceCalculationGroup("stop1", "Depot", [new PriceCalculationGroupUnit(euro, 3)], "depot"),
            new PriceCalculationGroup("stop2", "Depot", [new PriceCalculationGroupUnit(euro, 2)], "depot"),
        };

        var merged = CombinedUnitDiscountMath.MergeGroups(groups, DegressionScope.Stop);

        Assert.Equal(2, merged.Count);
        Assert.Equal(3, merged[0].Quantities[euro]);
        Assert.Equal(2, merged[1].Quantities[euro]);
    }

    [Fact]
    public void MergeGroups_DeliveryAddress_MergesSameAddressKey_KeepsNullAddressKeySeparate()
    {
        var euro = Guid.NewGuid();
        var groups = new[]
        {
            new PriceCalculationGroup("stop1", "Depot", [new PriceCalculationGroupUnit(euro, 3)], "depot"),
            new PriceCalculationGroup("stop2", "Depot", [new PriceCalculationGroupUnit(euro, 2)], "depot"),
            new PriceCalculationGroup("stop3", "Onbekend", [new PriceCalculationGroupUnit(euro, 1)], null),
        };

        var merged = CombinedUnitDiscountMath.MergeGroups(groups, DegressionScope.DeliveryAddress);

        Assert.Equal(2, merged.Count);
        var depot = merged.Single(g => g.Key == "depot");
        Assert.Equal(5, depot.Quantities[euro]);
        var unknown = merged.Single(g => g.Key == "stop3");
        Assert.Equal(1, unknown.Quantities[euro]);
    }

    [Fact]
    public void ComputeEquivalent_OnlyCountsConfiguredUnits_WeightedByFactor()
    {
        var euro = Guid.NewGuid();
        var colli = Guid.NewGuid();
        var notConfigured = Guid.NewGuid();
        var quantities = new Dictionary<Guid, decimal> { [euro] = 1, [colli] = 2, [notConfigured] = 99 };

        var equivalent = CombinedUnitDiscountMath.ComputeEquivalent(quantities, [(euro, 1m), (colli, 0.25m)]);

        Assert.Equal(1.5m, equivalent);
    }

    [Fact]
    public void FindTier_ReturnsMatchingRange_NullWhenNoneMatch()
    {
        var tiers = new List<CombinedUnitDiscountTier>
        {
            new() { FromCount = 2, ToCount = 3, Percent = 5 },
            new() { FromCount = 4, ToCount = 5, Percent = 8 },
            new() { FromCount = 6, ToCount = null, Percent = 10 },
        };

        Assert.Equal(5m, CombinedUnitDiscountMath.FindTier(tiers, 3)?.Percent);
        Assert.Equal(8m, CombinedUnitDiscountMath.FindTier(tiers, 5)?.Percent);
        Assert.Equal(10m, CombinedUnitDiscountMath.FindTier(tiers, 50)?.Percent);
        Assert.Null(CombinedUnitDiscountMath.FindTier(tiers, 1));
    }

    [Fact]
    public void ComputeEligibleBase_ApportionsByGroupShareOfTotalQuantity_ExcludesUnpricedOrUnconfiguredUnits()
    {
        var euro = Guid.NewGuid();
        var blok = Guid.NewGuid();
        var unpriced = Guid.NewGuid();
        var groupQuantities = new Dictionary<Guid, decimal> { [euro] = 3, [blok] = 1, [unpriced] = 10 };
        var totals = new Dictionary<Guid, decimal> { [euro] = 5, [blok] = 1 };
        var lineAmounts = new Dictionary<Guid, decimal> { [euro] = 250m, [blok] = 40m };

        var eligible = CombinedUnitDiscountMath.ComputeEligibleBase(groupQuantities, totals, lineAmounts, [euro, blok, unpriced]);

        // euro: 250 * (3/5) = 150; blok: 40 * (1/1) = 40; unpriced: no line amount => 0.
        Assert.Equal(190m, eligible);
    }

    [Fact]
    public void ComputeEligibleBase_ZeroTotalQuantity_NeverDividesByZero()
    {
        var euro = Guid.NewGuid();
        var eligible = CombinedUnitDiscountMath.ComputeEligibleBase(
            new Dictionary<Guid, decimal> { [euro] = 0 },
            new Dictionary<Guid, decimal> { [euro] = 0 },
            new Dictionary<Guid, decimal> { [euro] = 100m },
            [euro]);

        Assert.Equal(0m, eligible);
    }
}

/// <summary>
/// Combined-unit degression discounts end to end (spec §29-31): weighted equivalents across
/// different unit types, scope-driven grouping (order/delivery-address/stop), the eligible-base
/// apportioning, most-specific-wins selection with ambiguity blocking, and integration into the
/// canonical agreement post-processing order (base+modifiers → degression → assignment → min/max
/// → surcharges).
/// </summary>
public class CombinedUnitDiscountTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private sealed record Harness(
        SqliteTestDbContext? Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid EuroId, Guid BlokId, Guid HalveId, Guid ColliId);

    /// <summary>
    /// Seeds a fresh tenant/customer/unit types. Pass <paramref name="sharedContext"/> (from an
    /// existing <see cref="SqliteTestDbContext"/>) to seed a SECOND tenant into the SAME database
    /// for tenant-isolation tests — the returned harness then owns no disposable (the caller's
    /// `using var shared = new SqliteTestDbContext();` covers disposal).
    /// </summary>
    private static async Task<Harness> SeedAsync(TransportationDbContext? sharedContext = null)
    {
        SqliteTestDbContext? owned = null;
        TransportationDbContext context;
        if (sharedContext is null)
        {
            owned = new SqliteTestDbContext();
            context = owned.Context;
        }
        else
        {
            context = sharedContext;
        }

        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var euroId = Guid.NewGuid();
        var blokId = Guid.NewGuid();
        var halveId = Guid.NewGuid();
        var colliId = Guid.NewGuid();

        context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme-" + tenantId, IsActive = true, CreatedAt = DateTime.UtcNow });
        context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant X", CustomerNumber = "KL-" + tenantId.ToString()[..4], IsActive = true });
        context.UnitTypes.Add(new UnitType { Id = euroId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        context.UnitTypes.Add(new UnitType { Id = blokId, TenantId = tenantId, Code = "BLOKPALLET", Name = "Blokpallet", IsActive = true });
        context.UnitTypes.Add(new UnitType { Id = halveId, TenantId = tenantId, Code = "HALVEPALLET", Name = "Halve pallet", IsActive = true });
        context.UnitTypes.Add(new UnitType { Id = colliId, TenantId = tenantId, Code = "COLLI", Name = "Colli", IsActive = true });
        await context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(context, tenant, audit);
        var engine = new PricingEngine(context, tenant);
        return new Harness(owned, engine, admin, tenantId, customerId, euroId, blokId, halveId, colliId);
    }

    /// <summary>A company-wide (CustomerId null, IsShared false) agreement — always applicable, no assignment needed.</summary>
    private static Task<PricingAgreementDto> CreateAgreementAsync(
        Harness h, string name, bool isShared = false, Guid? customerId = null,
        decimal? minimumAmount = null) =>
        h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            customerId, name, Today.AddYears(-1), null, true, minimumAmount, null, null, IsShared: isShared), CancellationToken.None);

    private static Task<PriceRuleDto> CreateUnitRuleAsync(
        Harness h, Guid unitTypeId, decimal unitPrice, string name, Guid? agreementId = null, Guid? customerId = null) =>
        h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            customerId, unitTypeId, PriceRuleBasis.PerUnit, null,
            name, Today.AddYears(-1), null, true, unitPrice, null, null, AgreementId: agreementId), CancellationToken.None);

    private static Task<CombinedUnitDiscountDto> CreateDiscountAsync(
        Harness h, string name, IReadOnlyList<SaveCombinedUnitDiscountUnitRequest> units, IReadOnlyList<SaveCombinedUnitDiscountTierRequest> tiers,
        DegressionScope scope = DegressionScope.DeliveryAddress, Guid? customerId = null, Guid? agreementId = null,
        DateOnly? from = null, DateOnly? until = null, bool isActive = true) =>
        h.Admin.CreateCombinedDiscountAsync(new SaveCombinedUnitDiscountRequest(
            customerId, agreementId, name, scope, from ?? Today.AddYears(-1), until, isActive, units, tiers), CancellationToken.None);

    private static PriceCalculationRequest Request(
        Harness h, IReadOnlyList<PriceCalculationLineInput> lines, IReadOnlyList<PriceCalculationGroup>? groups = null, DateOnly? date = null) =>
        new(h.CustomerId, date ?? Today, lines, "BE", null, null, null, null, [], Groups: groups);

    // --- S13: three different unit types combine into one weighted equivalent ------------------

    [Fact]
    public async Task S13_ThreeUnitTypesCombine_MatchTierAndDiscountExactEligibleBase()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", agreement.Id);
        await CreateUnitRuleAsync(h, h.BlokId, 40m, "Blokpallet", agreement.Id);
        await CreateUnitRuleAsync(h, h.ColliId, 10m, "Colli", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting",
            [new(h.EuroId, 1m), new(h.BlokId, 1m), new(h.ColliId, 1m)],
            [new(2, 3, 5), new(4, 5, 8), new(6, null, 10)]);

        var result = await h.Engine.CalculateAsync(Request(h,
            [new PriceCalculationLineInput(h.EuroId, 1), new PriceCalculationLineInput(h.BlokId, 1), new PriceCalculationLineInput(h.ColliId, 2)]),
            CancellationToken.None);

        // Base: 40 + 40 + 20 = 100; equivalent 1+1+2=4 → tier 4-5 → -8% of 100 = -8.
        var discountLine = Assert.Single(result.Lines, l => l.Label.Contains("→ -8%"));
        Assert.Equal(-8m, discountLine.Amount);
        Assert.Equal(agreement.Id, discountLine.AgreementId);
        Assert.Equal(92m, result.Total);
    }

    // --- S14: weighted factors (halve pallet = 0.5, colli = 0.25) --------------------------------

    [Fact]
    public async Task S14_WeightedFactors_Equivalent2_MatchesLowestTier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", agreement.Id);
        await CreateUnitRuleAsync(h, h.HalveId, 20m, "Halve pallet", agreement.Id);
        await CreateUnitRuleAsync(h, h.ColliId, 10m, "Colli", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting",
            [new(h.EuroId, 1m), new(h.HalveId, 0.5m), new(h.ColliId, 0.25m)],
            [new(2, 3, 5), new(4, 5, 8), new(6, null, 10)]);

        var result = await h.Engine.CalculateAsync(Request(h,
            [new PriceCalculationLineInput(h.EuroId, 1), new PriceCalculationLineInput(h.HalveId, 1), new PriceCalculationLineInput(h.ColliId, 2)]),
            CancellationToken.None);

        // equivalent = 1*1 + 1*0.5 + 2*0.25 = 2.0 → tier 2-3 → -5%; base 40+20+20=80 → -4.
        var discountLine = Assert.Single(result.Lines, l => l.Label.StartsWith("Combikorting") && l.Label.Contains("→ -5%"));
        Assert.Equal(-4m, discountLine.Amount);
        Assert.Equal(76m, result.Total);
    }

    // --- S15/S16: DeliveryAddress scope keeps groups separate; Order scope merges them -----------

    [Fact]
    public async Task S15_DeliveryAddressScope_EachGroupGetsItsOwnTier_NeverTheCombinedTier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 50m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)],
            [new(2, 3, 5), new(4, 5, 8), new(6, null, 10)]);

        var groups = new[]
        {
            new PriceCalculationGroup("stopA", "Antwerpen", [new PriceCalculationGroupUnit(h.EuroId, 3)], "antwerpen"),
            new PriceCalculationGroup("stopB", "Mechelen", [new PriceCalculationGroupUnit(h.EuroId, 2)], "mechelen"),
        };

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 5)], groups), CancellationToken.None);

        // Base 250 (5 * 50). Antwerpen share 3/5*250=150 → -5% = -7.50. Mechelen share 2/5*250=100 → -5% = -5.00.
        Assert.DoesNotContain(result.Lines, l => l.Label.Contains("→ -8%"));
        var antwerpen = Assert.Single(result.Lines, l => l.Label.Contains("Antwerpen"));
        var mechelen = Assert.Single(result.Lines, l => l.Label.Contains("Mechelen"));
        Assert.Equal(-7.50m, antwerpen.Amount);
        Assert.Equal(-5.00m, mechelen.Amount);
        Assert.Equal(250m - 7.50m - 5.00m, result.Total);
    }

    [Fact]
    public async Task S16_OrderScope_MergesGroups_UsesCombinedTier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 50m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)],
            [new(2, 3, 5), new(4, 5, 8), new(6, null, 10)], scope: DegressionScope.Order);

        var groups = new[]
        {
            new PriceCalculationGroup("stopA", "Antwerpen", [new PriceCalculationGroupUnit(h.EuroId, 3)], "antwerpen"),
            new PriceCalculationGroup("stopB", "Mechelen", [new PriceCalculationGroupUnit(h.EuroId, 2)], "mechelen"),
        };

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 5)], groups), CancellationToken.None);

        // Merged equivalent = 5 → tier 4-5 → -8% of 250 = -20.
        var discountLine = Assert.Single(result.Lines, l => l.Label.Contains("→ -8%"));
        Assert.Equal(-20m, discountLine.Amount);
        Assert.Equal(230m, result.Total);
    }

    // --- Scope Stop vs DeliveryAddress merge behaviour --------------------------------------------

    [Fact]
    public async Task Scope_Stop_KeepsStopsSeparate_EvenWhenTheySharedAnAddress()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 50m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)],
            [new(2, 3, 5), new(4, 5, 8), new(6, null, 10)], scope: DegressionScope.Stop);

        var groups = new[]
        {
            new PriceCalculationGroup("stopA", "Depot", [new PriceCalculationGroupUnit(h.EuroId, 3)], "depot"),
            new PriceCalculationGroup("stopB", "Depot", [new PriceCalculationGroupUnit(h.EuroId, 2)], "depot"),
        };

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 5)], groups), CancellationToken.None);

        // Even though both stops share an address, Scope.Stop never merges them.
        Assert.DoesNotContain(result.Lines, l => l.Label.Contains("→ -8%"));
        Assert.Equal(2, result.Lines.Count(l => l.Label.StartsWith("Combikorting")));
    }

    [Fact]
    public async Task Scope_DeliveryAddress_MergesStopsSharingAddressKey()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 50m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)],
            [new(2, 3, 5), new(4, 5, 8), new(6, null, 10)], scope: DegressionScope.DeliveryAddress);

        var groups = new[]
        {
            new PriceCalculationGroup("stopA", "Depot", [new PriceCalculationGroupUnit(h.EuroId, 3)], "depot"),
            new PriceCalculationGroup("stopB", "Depot", [new PriceCalculationGroupUnit(h.EuroId, 2)], "depot"),
        };

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 5)], groups), CancellationToken.None);

        // Same AddressKey ⇒ merged into ONE group ⇒ combined tier (8%) fires exactly once.
        var discountLine = Assert.Single(result.Lines, l => l.Label.StartsWith("Combikorting"));
        Assert.Contains("→ -8%", discountLine.Label);
        Assert.Equal(-20m, discountLine.Amount);
    }

    // --- Selection: customer-scoped beats company-wide; ambiguity blocks --------------------------

    [Fact]
    public async Task CustomerScopedDiscount_BeatsCompanyWide()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Algemeen", [new(h.EuroId, 1m)], [new(1, null, 5)]); // company-wide
        await CreateDiscountAsync(h, "KlantX", [new(h.EuroId, 1m)], [new(1, null, 20)], customerId: h.CustomerId); // customer-specific

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 1)]), CancellationToken.None);

        var discountLine = Assert.Single(result.Lines, l => l.Label.StartsWith("KlantX") || l.Label.StartsWith("Algemeen"));
        Assert.StartsWith("KlantX", discountLine.Label);
        Assert.Equal(-8m, discountLine.Amount); // 40 * 20% = 8, customer-specific wins over the 5% company-wide one.
    }

    [Fact]
    public async Task SameSpecificityDuplicateDiscounts_BlocksWithConfigurationError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Korting A", [new(h.EuroId, 1m)], [new(1, null, 5)]);
        await CreateDiscountAsync(h, "Korting B", [new(h.EuroId, 1m)], [new(1, null, 8)]);

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 1)]), CancellationToken.None);

        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Conflicterende combinatiekortingen", result.ConfigurationError);
        Assert.True(result.RequiresManualPrice);
        Assert.DoesNotContain(result.Lines, l => l.Label.StartsWith("Korting"));
    }

    // --- Agreement-linked discount only fires when engaged -----------------------------------------

    [Fact]
    public async Task AgreementLinkedDiscount_OnlyFires_WhenThatAgreementIsEngaged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var engagedAgreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", engagedAgreement.Id);
        // A second, valid agreement that this order never engages (no rule of its own priced anything).
        var neverEngagedAgreement = await CreateAgreementAsync(h, "Nooit gebruikt");

        var linkedToUnused = await CreateDiscountAsync(
            h, "Combikorting", [new(h.EuroId, 1m)], [new(1, null, 10)], agreementId: neverEngagedAgreement.Id);
        var before = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 1)]), CancellationToken.None);
        Assert.DoesNotContain(before.Lines, l => l.Label.StartsWith("Combikorting"));
        Assert.Equal(40m, before.Total);

        await h.Admin.DeleteCombinedDiscountAsync(linkedToUnused.Id, CancellationToken.None);
        await CreateDiscountAsync(
            h, "Combikorting", [new(h.EuroId, 1m)], [new(1, null, 10)], agreementId: engagedAgreement.Id);
        var after = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 1)]), CancellationToken.None);

        Assert.Contains(after.Lines, l => l.Label.StartsWith("Combikorting"));
        Assert.Equal(36m, after.Total); // 40 - 10% = 36
    }

    // --- Canonical order: degression before assignment %, before minimum --------------------------

    [Fact]
    public async Task DiscountLine_JoinsAgreementSubtotal_BeforeAssignmentPercent_BeforeMinimum()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var shared = await CreateAgreementAsync(h, "Gedeelde tabel", isShared: true, minimumAmount: 70m);
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", shared.Id);
        await h.Admin.SaveAssignmentsAsync(shared.Id,
            [new SavePricingAssignmentRequest(h.CustomerId, -5m, null, null, null, null)], CancellationToken.None);
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)], [new(1, null, 10)]);

        var result = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 2)]), CancellationToken.None);

        // Base 80 → discount -10% = -8 (subtotal 72) → assignment -5% of 72 = -3.60 (subtotal 68.40)
        // → minimum 70 tops up +1.60 → total 70.
        var baseLine = Assert.Single(result.Lines, l => l.RuleName == "Europallet");
        var discountLine = Assert.Single(result.Lines, l => l.Label.StartsWith("Combikorting"));
        var assignLine = Assert.Single(result.Lines, l => l.Label.StartsWith("Klantafspraak"));
        var minLine = Assert.Single(result.Lines, l => l.Label.StartsWith("Minimumtarief"));
        Assert.Equal(80m, baseLine.Amount);
        Assert.Equal(-8m, discountLine.Amount);
        Assert.Equal(-3.60m, assignLine.Amount);
        Assert.Equal(1.60m, minLine.Amount);
        Assert.Equal(70m, result.Total);

        var order = result.Lines.ToList();
        Assert.True(order.IndexOf(baseLine) < order.IndexOf(discountLine));
        Assert.True(order.IndexOf(discountLine) < order.IndexOf(assignLine));
        Assert.True(order.IndexOf(assignLine) < order.IndexOf(minLine));
    }

    // --- Units not configured on the discount never count -------------------------------------------

    [Fact]
    public async Task UnconfiguredUnits_NeverCountTowardEquivalentOrEligibleBase()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", agreement.Id);
        await CreateUnitRuleAsync(h, h.BlokId, 100m, "Blokpallet", agreement.Id);
        // Discount only configured for Europallet — Blokpallet is priced but not part of the discount.
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)], [new(1, null, 10)]);

        var result = await h.Engine.CalculateAsync(Request(h,
            [new PriceCalculationLineInput(h.EuroId, 1), new PriceCalculationLineInput(h.BlokId, 5)]),
            CancellationToken.None);

        var discountLine = Assert.Single(result.Lines, l => l.Label.StartsWith("Combikorting"));
        // equivalent purely from Europallet (1) → -10% of 40 = -4, never touching the 500 Blokpallet base.
        Assert.Equal(-4m, discountLine.Amount);
        Assert.Equal(40m + 500m - 4m, result.Total);
    }

    // --- Effective window respected; tenant isolation ------------------------------------------------

    [Fact]
    public async Task EffectiveWindow_OutsideRange_NeverApplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Standaard");
        await CreateUnitRuleAsync(h, h.EuroId, 40m, "Europallet", agreement.Id);
        await CreateDiscountAsync(h, "Combikorting", [new(h.EuroId, 1m)], [new(1, null, 10)],
            from: Today.AddDays(10), until: null);

        var before = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 1)], date: Today), CancellationToken.None);
        var within = await h.Engine.CalculateAsync(
            Request(h, [new PriceCalculationLineInput(h.EuroId, 1)], date: Today.AddDays(10)), CancellationToken.None);

        Assert.DoesNotContain(before.Lines, l => l.Label.StartsWith("Combikorting"));
        Assert.Contains(within.Lines, l => l.Label.StartsWith("Combikorting"));
    }

    [Fact]
    public async Task TenantIsolation_DiscountFromOtherTenant_NeverApplies()
    {
        using var shared = new SqliteTestDbContext();
        var h1 = await SeedAsync(shared.Context);
        var h2 = await SeedAsync(shared.Context);

        var agreement1 = await CreateAgreementAsync(h1, "Standaard tenant 1");
        await CreateUnitRuleAsync(h1, h1.EuroId, 40m, "Europallet", agreement1.Id);
        await CreateDiscountAsync(h1, "Combikorting", [new(h1.EuroId, 1m)], [new(1, null, 10)]);

        var agreement2 = await CreateAgreementAsync(h2, "Standaard tenant 2");
        await CreateUnitRuleAsync(h2, h2.EuroId, 40m, "Europallet", agreement2.Id);
        // Tenant 2 has NO discount configured — its own calculation must never see tenant 1's discount.

        var result2 = await h2.Engine.CalculateAsync(
            Request(h2, [new PriceCalculationLineInput(h2.EuroId, 1)]), CancellationToken.None);

        Assert.DoesNotContain(result2.Lines, l => l.Label.StartsWith("Combikorting"));
        Assert.Equal(40m, result2.Total);

        var tenant2Discounts = await h2.Admin.ListCombinedDiscountsAsync(null, null, CancellationToken.None);
        Assert.Empty(tenant2Discounts);
    }
}
