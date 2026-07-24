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
}
