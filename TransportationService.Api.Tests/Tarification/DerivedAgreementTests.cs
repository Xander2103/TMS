using Microsoft.EntityFrameworkCore;
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
/// Derived rate tables (spec §9, "NL = BE +30%"): a derived agreement has no rules of its own —
/// it reuses its base-chain root's rules and stacks its own country/zone-conditioned modifiers
/// on the running subtotal, before assignment adjustment/minimum/maximum/surcharges (§33 order).
/// </summary>
public class DerivedAgreementTests
{
    private static readonly DateOnly Today = new(2026, 7, 25);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerAId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerAId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerAId, palletUnitId);
    }

    private static PriceCalculationRequest Request(
        Harness h, decimal quantity, string? deliveryCountry = "BE", string? postalCode = null, DateOnly? date = null) =>
        new(h.CustomerAId, date ?? Today, [new PriceCalculationLineInput(h.PalletUnitId, quantity)],
            deliveryCountry, postalCode, null, null, null, []);

    /// <summary>A shared, company-wide table by default — assign customers to make it engage.</summary>
    private static Task<PricingAgreementDto> CreateAgreementAsync(
        Harness h, string name, bool isShared = true, Guid? customerId = null,
        Guid? baseAgreementId = null, IReadOnlyList<SavePricingAgreementModifierRequest>? modifiers = null) =>
        h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            customerId, name, Today.AddMonths(-2), null, true, null, null, null,
            IsShared: isShared, BaseAgreementId: baseAgreementId, Modifiers: modifiers), CancellationToken.None);

    private static Task<PriceRuleDto> CreatePalletRuleAsync(
        Harness h, Guid agreementId, decimal unitPrice, string name = "Pallets BE",
        DateOnly? from = null, DateOnly? until = null) =>
        h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            name, from ?? Today.AddMonths(-2), until, true, unitPrice, null, null,
            AgreementId: agreementId), CancellationToken.None);

    [Fact]
    public async Task Derived_AppliesBaseRulePlusMatchingCountryModifier_S4()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var be = await CreateAgreementAsync(h, "Distributie België");
        await CreatePalletRuleAsync(h, be.Id, 50m);
        var nl = await CreateAgreementAsync(h, "NL Distributie", baseAgreementId: be.Id,
            modifiers: [new SavePricingAgreementModifierRequest(1, "Nederland +30%", "NL", null, 30m, null)]);
        await h.Admin.SaveAssignmentsAsync(nl.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 1, deliveryCountry: "NL"), CancellationToken.None);

        Assert.Contains(result.Lines, l => l.Amount == 50m && l.AgreementId == nl.Id && l.RuleName == "Pallets BE");
        Assert.Contains(result.Lines, l => l.Label == "Nederland +30%" && l.Amount == 15m && l.AgreementId == nl.Id);
        Assert.Equal(65m, result.Total);
    }

    [Fact]
    public async Task Derived_NewBaseRuleVersion_ChangesDerivedPriceWithoutTouchingModifierConfig_S5()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var be = await CreateAgreementAsync(h, "Distributie België");
        await CreatePalletRuleAsync(h, be.Id, 50m, until: Today.AddDays(9));
        await CreatePalletRuleAsync(h, be.Id, 55m, name: "Pallets BE v2", from: Today.AddDays(10));
        var nl = await CreateAgreementAsync(h, "NL Distributie", baseAgreementId: be.Id,
            modifiers: [new SavePricingAgreementModifierRequest(1, "Nederland +30%", "NL", null, 30m, null)]);
        await h.Admin.SaveAssignmentsAsync(nl.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var before = await h.Engine.CalculateAsync(Request(h, 1, deliveryCountry: "NL"), CancellationToken.None);
        var after = await h.Engine.CalculateAsync(Request(h, 1, deliveryCountry: "NL", date: Today.AddDays(10)), CancellationToken.None);

        Assert.Equal(65m, before.Total);
        Assert.Equal(71.50m, after.Total);
    }

    [Fact]
    public async Task Derived_StacksSecondZoneModifier_OnlyWhenZoneMatches()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zone = await h.Admin.CreateZoneAsync(new SavePricingZoneRequest("W", "Waddeneilanden", true, 0,
            [new SavePricingZoneAreaRequest("NL", "9000", "9999")]), CancellationToken.None);
        var be = await CreateAgreementAsync(h, "Distributie België");
        await CreatePalletRuleAsync(h, be.Id, 50m);
        var nl = await CreateAgreementAsync(h, "NL Distributie", baseAgreementId: be.Id,
            modifiers:
            [
                new SavePricingAgreementModifierRequest(1, "Nederland +30%", "NL", null, 30m, null),
                new SavePricingAgreementModifierRequest(2, "Waddeneilanden +€75", null, zone.Id, null, 75m),
            ]);
        await h.Admin.SaveAssignmentsAsync(nl.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var matching = await h.Engine.CalculateAsync(Request(h, 1, deliveryCountry: "NL", postalCode: "9010"), CancellationToken.None);
        var nonMatching = await h.Engine.CalculateAsync(Request(h, 1, deliveryCountry: "NL", postalCode: "1000"), CancellationToken.None);

        Assert.Equal(140m, matching.Total);
        Assert.Equal(65m, nonMatching.Total);
    }

    [Fact]
    public async Task Derived_ModifierConditionNotMatching_ProducesNoModifierLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var be = await CreateAgreementAsync(h, "Distributie België");
        await CreatePalletRuleAsync(h, be.Id, 50m);
        var nl = await CreateAgreementAsync(h, "NL Distributie", baseAgreementId: be.Id,
            modifiers: [new SavePricingAgreementModifierRequest(1, "Nederland +30%", "NL", null, 30m, null)]);
        await h.Admin.SaveAssignmentsAsync(nl.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 1, deliveryCountry: "BE"), CancellationToken.None);

        Assert.DoesNotContain(result.Lines, l => l.Label == "Nederland +30%");
        Assert.Equal(50m, result.Total);
    }

    [Fact]
    public async Task Derived_PlusAssignmentAdjustment_AppliesModifiersBeforeAssignment_S21()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var be = await CreateAgreementAsync(h, "Distributie België");
        await CreatePalletRuleAsync(h, be.Id, 50m);
        var nl = await CreateAgreementAsync(h, "NL Distributie", baseAgreementId: be.Id,
            modifiers: [new SavePricingAgreementModifierRequest(1, "Nederland +30%", "NL", null, 30m, null)]);
        await h.Admin.SaveAssignmentsAsync(nl.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, -5m, null, null, null, null)], CancellationToken.None);

        // 2 pallets x €50 = 100 -> +30% = 130 -> assignment -5% = -6.50 -> 123.50 (spec §33/§S21 order).
        var result = await h.Engine.CalculateAsync(Request(h, 2, deliveryCountry: "NL"), CancellationToken.None);

        Assert.Equal(123.50m, result.Total);
    }

    [Fact]
    public async Task Validation_CycleDepthAndOwnRulesRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var a = await CreateAgreementAsync(h, "Tabel A");
        var b = await CreateAgreementAsync(h, "Tabel B", baseAgreementId: a.Id);

        // A → B → A: updating A to derive from B (which already derives from A) is circular.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.UpdateAgreementAsync(a.Id,
            new SavePricingAgreementRequest(null, "Tabel A", Today.AddMonths(-2), null, true, null, null, null,
                IsShared: true, BaseAgreementId: b.Id), CancellationToken.None));

        // A (root, depth 0) <- B (depth 1) <- C (depth 2) <- D (depth 3): allowed.
        var c = await CreateAgreementAsync(h, "Tabel C", baseAgreementId: b.Id);
        var d = await CreateAgreementAsync(h, "Tabel D", baseAgreementId: c.Id);

        // E deriving from D would be depth 4: rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(null, "Tabel E", Today.AddMonths(-2), null, true, null, null, null,
                IsShared: true, BaseAgreementId: d.Id), CancellationToken.None));

        // A derived table cannot get its own price rule.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.PerUnit, null, "Eigen regel", Today.AddMonths(-2), null, true, 10m, null, null,
            AgreementId: b.Id), CancellationToken.None));

        // A table that already has its own rules cannot be converted into a derived table.
        var withRules = await CreateAgreementAsync(h, "Tabel Met Regels", isShared: false, customerId: h.CustomerAId);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, h.PalletUnitId, PriceRuleBasis.PerUnit, null, "Regel", Today.AddMonths(-2), null, true, 10m, null, null,
            AgreementId: withRules.Id), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.UpdateAgreementAsync(withRules.Id,
            new SavePricingAgreementRequest(h.CustomerAId, "Tabel Met Regels", Today.AddMonths(-2), null, true, null, null, null,
                BaseAgreementId: a.Id), CancellationToken.None));

        // Deleting a base table that other tables derive from is rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.DeleteAgreementAsync(a.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RuntimeGuard_ManufacturedCycle_ReturnsConfigurationErrorInsteadOfHanging()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        // Bypasses PricingAdminService validation entirely — a cycle that should never be
        // save-able must still not hang or stack-overflow the engine at read time. Two separate
        // saves (insert, then update) avoid EF's insert-order cycle detection, which would
        // otherwise refuse to persist a genuine FK cycle in one batch.
        h.Db.Context.PricingAgreements.Add(new PricingAgreement
        {
            Id = aId, TenantId = h.TenantId, Name = "Cyclus A", CustomerId = h.CustomerAId,
            EffectiveFrom = Today.AddMonths(-1), IsActive = true,
        });
        h.Db.Context.PricingAgreements.Add(new PricingAgreement
        {
            Id = bId, TenantId = h.TenantId, Name = "Cyclus B", CustomerId = Guid.NewGuid(),
            EffectiveFrom = Today.AddMonths(-1), IsActive = true, BaseAgreementId = aId,
        });
        await h.Db.Context.SaveChangesAsync();

        var trackedA = await h.Db.Context.PricingAgreements.FirstAsync(x => x.Id == aId);
        trackedA.BaseAgreementId = bId;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Engine.CalculateAsync(Request(h, 1), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Circulaire", result.ConfigurationError);
    }

    [Fact]
    public async Task TenantIsolation_BaseAgreementMustBeSameTenant_AndEngineNeverCrossesTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var be = await CreateAgreementAsync(h, "Distributie België");
        await CreatePalletRuleAsync(h, be.Id, 50m);

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        var foreignAgreementId = Guid.NewGuid();
        h.Db.Context.PricingAgreements.Add(new PricingAgreement
        {
            Id = foreignAgreementId, TenantId = foreignTenantId, Name = "Vreemde tabel",
            EffectiveFrom = Today.AddMonths(-1), IsActive = true, IsShared = true,
        });
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = foreignTenantId, Name = "Ander", CustomerNumber = "X-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        // A derived table cannot reference a base agreement from another tenant.
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(null, "NL Distributie", Today.AddMonths(-1), null, true, null, null, null,
                IsShared: true, BaseAgreementId: foreignAgreementId), CancellationToken.None));

        // The engine, run under the foreign tenant, never sees the main tenant's derived setup.
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignEngine = new PricingEngine(h.Db.Context, foreignTenant);
        var result = await foreignEngine.CalculateAsync(
            new PriceCalculationRequest(otherCustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, 1)], "NL", null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
    }
}
