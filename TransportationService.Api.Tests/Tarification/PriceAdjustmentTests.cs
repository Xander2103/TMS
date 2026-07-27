using System.Reflection;
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

/// <summary>Scheduled future price changes: preview, rounding, versioning, cancel, audit.</summary>
public class PriceAdjustmentTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, PriceAdjustmentService Sut, PricingAdminService Admin, PricingEngine Engine,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid HourUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var hourUnitId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = hourUnitId, TenantId = tenantId, Code = "UUR", Name = "Uur", IsActive = true });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var sut = new PriceAdjustmentService(db.Context, tenant, audit, new TestClock(Now));
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, sut, admin, engine, tenantId, customerId, palletUnitId, hourUnitId);
    }

    /// <summary>Seeds the spec §14 example: Brussels quantity brackets + an hourly rate.</summary>
    private static async Task SeedSpecExampleRulesAsync(Harness h)
    {
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Europallet Brussel", new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30), true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 45m, null),
                new SavePriceRuleBracketRequest(2, 2, 70m, null),
                new SavePriceRuleBracketRequest(3, null, 90m, null),
            ]), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.HourUnitId, PriceRuleBasis.Hourly, null,
            "Uurtarief", new DateOnly(2026, 1, 1), null, true, 72m, null, null), CancellationToken.None);
    }

    [Fact]
    public async Task Preview_ShowsSpecExampleValues_WithCorrectRounding()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);

        var preview = await h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, 4m, null), CancellationToken.None);

        Assert.Equal(2, preview.Count);
        var brackets = preview.Single(p => p.RuleName == "Europallet Brussel");
        // Spec §14: 45→46.80, 70→72.80, 90→93.60.
        Assert.Contains(brackets.Changes, c => c.OldValue == 45m && c.NewValue == 46.80m);
        Assert.Contains(brackets.Changes, c => c.OldValue == 70m && c.NewValue == 72.80m);
        Assert.Contains(brackets.Changes, c => c.OldValue == 90m && c.NewValue == 93.60m);
        var hourly = preview.Single(p => p.RuleName == "Uurtarief");
        // Spec §14: 72 → 74.88.
        Assert.Contains(hourly.Changes, c => c.OldValue == 72m && c.NewValue == 74.88m);
    }

    [Fact]
    public async Task Preview_SupportsDecrease()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);

        var preview = await h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, -2.5m, null), CancellationToken.None);

        var hourly = preview.Single(p => p.RuleName == "Uurtarief");
        Assert.Contains(hourly.Changes, c => c.OldValue == 72m && c.NewValue == 70.20m);
    }

    /// <summary>
    /// Ledger cleanup (Phase 10): defense-in-depth guard on the shared Validate helper — a request
    /// scoped to BOTH a customer AND an agreement must never be silently resolved to one. This is
    /// structurally unreachable through the public API today (every public method always passes
    /// exactly one of customerId/agreementId), so the guard is exercised directly via reflection.
    /// </summary>
    [Fact]
    public async Task Validate_BothCustomerAndAgreementScoped_ThrowsDomainValidationException()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var method = typeof(PriceAdjustmentService).GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var wrapped = Assert.Throws<TargetInvocationException>(() => method.Invoke(
            h.Sut, [October, 10m, null, null, h.CustomerId, Guid.NewGuid()]));

        var inner = Assert.IsType<DomainValidationException>(wrapped.InnerException);
        Assert.Equal("Kies precies één toepassingsgebied.", inner.Message);
    }

    [Fact]
    public async Task Create_MaterializesFutureVersions_CurrentPricesUnchangedBeforeDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);

        var created = await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, 4m, null, "Jaarlijkse indexatie"), CancellationToken.None);
        Assert.Equal("Gepland", created.Status);
        Assert.Equal(2, created.RuleCount);

        // The engine keeps pricing the OLD version before 1 October…
        var before = await h.Engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerId, new DateOnly(2026, 9, 15),
            [new PriceCalculationLineInput(h.PalletUnitId, 3)], "BE", null, null, null, null, []), CancellationToken.None);
        Assert.Equal(90m, before.Total);

        // …and the NEW version from 1 October (spec §15).
        var after = await h.Engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerId, new DateOnly(2026, 10, 15),
            [new PriceCalculationLineInput(h.PalletUnitId, 3)], "BE", null, null, null, null, []), CancellationToken.None);
        Assert.Equal(93.60m, after.Total);

        // History is versions, not overwrites: the source keeps its closed window.
        var rules = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Europallet Brussel")
            .OrderBy(r => r.EffectiveFrom).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), rules[0].EffectiveUntil);
        Assert.Equal(October, rules[1].EffectiveFrom);

        // Audit trail exists.
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(),
            l => l.EntityType == "ScheduledPriceAdjustment" && l.Action == "Created");
    }

    [Fact]
    public async Task Create_OpenEndedSource_ClosesDayBeforeEffectiveDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);

        await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, 4m, null, null), CancellationToken.None);

        var hourly = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Uurtarief")
            .OrderBy(r => r.EffectiveFrom).ToListAsync();
        Assert.Equal(new DateOnly(2026, 9, 30), hourly[0].EffectiveUntil);
        Assert.Null(hourly[1].EffectiveUntil);
        Assert.Equal(74.88m, hourly[1].UnitPrice);
    }

    [Fact]
    public async Task Create_WithRuleSelection_OnlyAdjustsSelected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);
        var hourlyRule = (await h.Admin.ListRulesAsync(h.CustomerId, CancellationToken.None))
            .Single(r => r.Name == "Uurtarief");

        var created = await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, 4m, [hourlyRule.Id], null), CancellationToken.None);

        Assert.Equal(1, created.RuleCount);
        Assert.Equal(1, await h.Db.Context.PriceRules.CountAsync(
            r => r.TenantId == h.TenantId && r.Name == "Europallet Brussel"));
        Assert.Equal(2, await h.Db.Context.PriceRules.CountAsync(
            r => r.TenantId == h.TenantId && r.Name == "Uurtarief"));
    }

    [Fact]
    public async Task Cancel_BeforeActivation_RestoresEverything()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);
        var created = await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, 4m, null, null), CancellationToken.None);

        var cancelled = await h.Sut.CancelAsync(h.CustomerId, created.Id, CancellationToken.None);

        Assert.Equal("Geannuleerd", cancelled!.Status);
        var hourly = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Uurtarief").ToListAsync();
        var restoredHourly = Assert.Single(hourly);       // future version removed
        Assert.Null(restoredHourly.EffectiveUntil);        // original open window restored
        var brackets = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Europallet Brussel").ToListAsync();
        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(brackets).EffectiveUntil);
    }

    [Fact]
    public async Task Cancel_AfterActivation_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);
        // Schedule effective tomorrow, then "wait" past it with a later clock.
        var created = await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(Today.AddDays(1), 4m, null, null), CancellationToken.None);
        var tenant = new DevTenantContext(h.TenantId);
        var laterSut = new PriceAdjustmentService(h.Db.Context, tenant,
            new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)),
            new TestClock(Now.AddDays(5)));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => laterSut.CancelAsync(h.CustomerId, created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Validation_RejectsPastDate_ZeroAndExtremePercent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(Today, 4m, null), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, 0m, null), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, 150m, null), CancellationToken.None));
        // A selected rule of another customer/nonexistent is refused, not silently skipped.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, 4m, [Guid.NewGuid()], null), CancellationToken.None));
    }

    [Fact]
    public async Task Adjustments_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);
        await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, 4m, null, null), CancellationToken.None);

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignSut = new PriceAdjustmentService(h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)), new TestClock(Now));

        Assert.Empty(await foreignSut.ListAsync(h.CustomerId, CancellationToken.None));
        Assert.Null(await foreignSut.CancelAsync(h.CustomerId,
            (await h.Sut.ListAsync(h.CustomerId, CancellationToken.None))[0].Id, CancellationToken.None));
    }

    // --- v2: agreement scope, AmountDelta, RoundingStep, basis/unit filters ---

    [Fact]
    public async Task AgreementScope_PlusFourPercent_PreviewAndConfirm_MirrorsCustomerFlow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Distributie 2026", new DateOnly(2026, 1, 1), null, true, null, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Europallet Brussel", new DateOnly(2026, 1, 1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 45m, null),
                new SavePriceRuleBracketRequest(2, null, 70m, null),
            ],
            agreement.Id), CancellationToken.None);

        var preview = await h.Sut.PreviewForAgreementAsync(agreement.Id,
            new PreviewPriceAdjustmentRequest(October, 4m, null), CancellationToken.None);
        var previewedRule = Assert.Single(preview);
        Assert.Contains(previewedRule.Changes, c => c.OldValue == 45m && c.NewValue == 46.80m);
        Assert.Contains(previewedRule.Changes, c => c.OldValue == 70m && c.NewValue == 72.80m);

        var created = await h.Sut.CreateForAgreementAsync(agreement.Id,
            new CreatePriceAdjustmentRequest(October, 4m, null, "Jaarlijkse indexatie"), CancellationToken.None);
        Assert.Equal("Gepland", created.Status);
        Assert.Equal(1, created.RuleCount);

        var rules = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Europallet Brussel")
            .OrderBy(r => r.EffectiveFrom).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), rules[0].EffectiveUntil);
        Assert.Equal(October, rules[1].EffectiveFrom);

        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(),
            l => l.EntityType == "ScheduledPriceAdjustment" && l.Action == "Created");
    }

    [Fact]
    public async Task Create_AmountDeltaWithBasisFilter_OnlyAdjustsMatchingBasisRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.HourUnitId, PriceRuleBasis.Hourly, null,
            "Uurtarief", new DateOnly(2026, 1, 1), null, true, 95m, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Palletprijs", new DateOnly(2026, 1, 1), null, true, 12m, null, null), CancellationToken.None);

        var created = await h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, null, null, null, AmountDelta: 5m, BasisFilter: "Hourly"),
            CancellationToken.None);

        Assert.Equal(1, created.RuleCount);
        var hourly = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Uurtarief")
            .OrderBy(r => r.EffectiveFrom).ToListAsync();
        Assert.Equal(2, hourly.Count);
        Assert.Equal(100m, hourly[1].UnitPrice);
        // The PerUnit rule sits outside the basis filter and keeps its single version.
        Assert.Equal(1, await h.Db.Context.PriceRules.CountAsync(r => r.TenantId == h.TenantId && r.Name == "Palletprijs"));
    }

    [Fact]
    public async Task Preview_RoundingStep_RoundsToNearestStepAwayFromZero()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.HourUnitId, PriceRuleBasis.Hourly, null,
            "Uurtarief", new DateOnly(2026, 1, 1), null, true, 47.75m, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Palletprijs", new DateOnly(2026, 1, 1), null, true, 45m, null, null), CancellationToken.None);

        // -2% on 47.75 → 46.795, which rounds up (away from zero) to the nearest 0,05 → 46.80.
        var decreasePreview = await h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, -2m, null, RoundingStep: 0.05m), CancellationToken.None);
        var hourlyChange = decreasePreview.Single(p => p.RuleName == "Uurtarief");
        Assert.Contains(hourlyChange.Changes, c => c.OldValue == 47.75m && c.NewValue == 46.80m);

        // 45 × 1.04 = 46.80 exactly: already a multiple of 0,05 and stays put.
        var increasePreview = await h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, 4m, null, RoundingStep: 0.05m), CancellationToken.None);
        var palletChange = increasePreview.Single(p => p.RuleName == "Palletprijs");
        Assert.Contains(palletChange.Changes, c => c.OldValue == 45m && c.NewValue == 46.80m);
    }

    [Fact]
    public async Task Create_AmountDelta_ProducingNegative_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Palletprijs", new DateOnly(2026, 1, 1), null, true, 3m, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(h.CustomerId,
            new CreatePriceAdjustmentRequest(October, null, null, null, AmountDelta: -10m), CancellationToken.None));
    }

    [Fact]
    public async Task Validation_RequiresExactlyOnePercentOrAmountDelta_AndValidRoundingStep()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedSpecExampleRulesAsync(h);

        // Neither Percent nor AmountDelta set.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, null, null), CancellationToken.None));
        // Both set.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, 4m, null, AmountDelta: 5m), CancellationToken.None));
        // Invalid rounding step.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.PreviewAsync(h.CustomerId,
            new PreviewPriceAdjustmentRequest(October, 4m, null, RoundingStep: 0.02m), CancellationToken.None));
    }

    [Fact]
    public async Task AgreementScope_Cancel_RestoresWindows()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Distributie 2026", new DateOnly(2026, 1, 1), null, true, null, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.HourUnitId, PriceRuleBasis.Hourly, null,
            "Uurtarief", new DateOnly(2026, 1, 1), null, true, 72m, null, null, agreement.Id), CancellationToken.None);

        var created = await h.Sut.CreateForAgreementAsync(agreement.Id,
            new CreatePriceAdjustmentRequest(October, 4m, null, null), CancellationToken.None);

        var cancelled = await h.Sut.CancelForAgreementAsync(agreement.Id, created.Id, CancellationToken.None);

        Assert.Equal("Geannuleerd", cancelled!.Status);
        var rules = await h.Db.Context.PriceRules
            .Where(r => r.TenantId == h.TenantId && r.Name == "Uurtarief").ToListAsync();
        var restored = Assert.Single(rules);
        Assert.Null(restored.EffectiveUntil);
    }
}
