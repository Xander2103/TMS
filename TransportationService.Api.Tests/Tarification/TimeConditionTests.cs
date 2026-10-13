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
/// Wave 2026-08-04 §16/§17: configurable time-based surcharges. A service option can carry
/// StopTimeBefore/StopTimeAfter/AppointmentRequired/Weekend conditions evaluated against the
/// per-stop time requirements; competing Before (resp. After) matches never double-charge —
/// highest priority, then the most specific time wins; an exact tie blocks with a
/// configuration error. Amounts and times come from configuration, never from code.
/// </summary>
public class TimeConditionTests
{
    private static readonly DateOnly Today = new(2026, 8, 4);

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
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerId, palletUnitId);
    }

    private static PriceCalculationRequest Request(Harness h, IReadOnlyList<StopTimeInput>? stopTimes = null) =>
        new(h.CustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, 3)], "BE", null, null, null, null,
            [], StopTimes: stopTimes);

    private static StopTimeInput Unloading(
        string kind = "None", TimeOnly? from = null, TimeOnly? to = null,
        bool appointment = false, DateOnly? plannedDate = null) =>
        new(true, kind, from, to, appointment, plannedDate);

    private static async Task SeedBaseRuleAsync(Harness h) =>
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", Today.AddMonths(-1), null, true, 10m, null, null), CancellationToken.None);

    private static Task<ServiceOptionDto> CreateTimeOptionAsync(
        Harness h, string code, string name, decimal amount,
        ServiceConditionKind kind, TimeOnly? time = null, int priority = 0, bool allowStacking = false,
        ServiceConditionStopScope scope = ServiceConditionStopScope.Unloading) =>
        h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            code, name, SurchargeKind.Fixed, amount, true, 0,
            AutoApply: true,
            TimeConditions: [new ServiceTimeConditionDto(kind, scope, time, priority, allowStacking)]), CancellationToken.None)!;

    [Fact]
    public async Task DeliveryBefore10_AutoApplies_OnlyWhenTheStopPromisesBefore10OrEarlier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "VOOR10", "Levering vóór 10u", 35m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0));

        // "Leveren vóór 09:30" promises 09:30 ≤ 10:00 → surcharge applies.
        var match = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Before", to: new TimeOnly(9, 30))]), CancellationToken.None);
        Assert.Equal(65m, match.Total); // 3 × €10 + €35
        Assert.Contains(match.ServiceLines, l => l.Name == "Levering vóór 10u" && l.AutoApplied);

        // "Leveren vóór 12:00" is a laxer promise than the condition → no surcharge.
        var laxer = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Before", to: new TimeOnly(12, 0))]), CancellationToken.None);
        Assert.Equal(30m, laxer.Total);

        // No time requirement → no surcharge.
        var none = await h.Engine.CalculateAsync(Request(h, [Unloading()]), CancellationToken.None);
        Assert.Equal(30m, none.Total);
    }

    [Fact]
    public async Task Before8WithHigherPriority_WinsOverBefore10_NeverDoubleCharges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "VOOR10", "Levering vóór 10u", 35m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0));
        await CreateTimeOptionAsync(h, "VOOR8", "Levering vóór 8u", 75m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(8, 0), priority: 1);

        // 07:30 matches both conditions — only the higher-priority "vóór 8u" is charged.
        var result = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Before", to: new TimeOnly(7, 30))]), CancellationToken.None);

        Assert.Equal(105m, result.Total); // 30 + 75, never 30 + 75 + 35
        Assert.Single(result.ServiceLines);
        Assert.Contains(result.ServiceLines, l => l.Name == "Levering vóór 8u");
        // §17: calculation details explain why the competing rule did not apply.
        Assert.Contains(result.Lines, l => l.Informational
            && l.Label.Contains("Levering vóór 10u: niet toegepast")
            && l.Label.Contains("Levering vóór 8u"));
    }

    [Fact]
    public async Task EqualPriority_SameTime_IsABlockingConfigurationError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "OCHT1", "Ochtendlevering A", 35m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0));
        await CreateTimeOptionAsync(h, "OCHT2", "Ochtendlevering B", 40m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0));

        var result = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Before", to: new TimeOnly(9, 0))]), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Conflicterende tijdsvoorwaarden", result.ConfigurationError);
        Assert.Empty(result.ServiceLines); // neither is silently charged
    }

    [Fact]
    public async Task EveningDelivery_After18_AppliesOnAfterOrWindowRequirements()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "AVOND", "Avondlevering", 40m,
            ServiceConditionKind.StopTimeAfter, new TimeOnly(18, 0));

        var evening = await h.Engine.CalculateAsync(
            Request(h, [Unloading("After", from: new TimeOnly(19, 0))]), CancellationToken.None);
        Assert.Equal(70m, evening.Total);

        var window = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Window", from: new TimeOnly(18, 30), to: new TimeOnly(20, 0))]), CancellationToken.None);
        Assert.Equal(70m, window.Total);

        var daytime = await h.Engine.CalculateAsync(
            Request(h, [Unloading("After", from: new TimeOnly(8, 0))]), CancellationToken.None);
        Assert.Equal(30m, daytime.Total);
    }

    [Fact]
    public async Task AppointmentSurcharge_AppliesWhenAStopRequiresAnAppointment()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "AFSPR", "Afspraaklevering", 20m,
            ServiceConditionKind.AppointmentRequired);

        var with = await h.Engine.CalculateAsync(
            Request(h, [Unloading(appointment: true)]), CancellationToken.None);
        Assert.Equal(50m, with.Total);

        var without = await h.Engine.CalculateAsync(Request(h, [Unloading()]), CancellationToken.None);
        Assert.Equal(30m, without.Total);
    }

    [Fact]
    public async Task WeekendSurcharge_AppliesOnSaturdayPlannedDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "WKND", "Weekendlevering", 40m,
            ServiceConditionKind.Weekend);

        var saturday = await h.Engine.CalculateAsync(
            Request(h, [Unloading(plannedDate: new DateOnly(2026, 8, 8))]), CancellationToken.None);
        Assert.Equal(70m, saturday.Total);

        var tuesday = await h.Engine.CalculateAsync(
            Request(h, [Unloading(plannedDate: new DateOnly(2026, 8, 4))]), CancellationToken.None);
        Assert.Equal(30m, tuesday.Total);
    }

    [Fact]
    public async Task AllowStacking_OptsOutOfTheCompetition()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "VOOR10", "Levering vóór 10u", 35m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0));
        await CreateTimeOptionAsync(h, "OCHTA", "Ochtendtoeslag admin", 5m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0), allowStacking: true);

        var result = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Before", to: new TimeOnly(9, 0))]), CancellationToken.None);

        // The stacking surcharge applies NEXT TO the winner — explicitly configured (§17).
        Assert.Equal(70m, result.Total); // 30 + 35 + 5
        Assert.Equal(2, result.ServiceLines.Count);
    }

    [Fact]
    public async Task ExplicitSelection_WithUnmetTimeCondition_IsInformationalNeverCharged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        var option = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "VOOR10", "Levering vóór 10u", SurchargeKind.Fixed, 35m, true, 0,
            AutoApply: false,
            TimeConditions: [new ServiceTimeConditionDto(
                ServiceConditionKind.StopTimeBefore, ServiceConditionStopScope.Unloading, new TimeOnly(10, 0))]),
            CancellationToken.None);

        var result = await h.Engine.CalculateAsync(
            Request(h, [Unloading()]) with { ServiceOptionIds = [option.Id] }, CancellationToken.None);

        Assert.Equal(30m, result.Total);
        Assert.Empty(result.ServiceLines);
        Assert.Contains(result.Lines, l => l.Informational
            && l.Label.Contains("alleen van toepassing bij de ingestelde tijdseis"));
    }

    [Fact]
    public async Task LoadingScopedCondition_IgnoresUnloadingStops()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "VROEG", "Vroeg laden", 25m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(8, 0), scope: ServiceConditionStopScope.Loading);

        // Only an unloading stop promises before 08:00 → the loading-scoped surcharge stays off.
        var result = await h.Engine.CalculateAsync(
            Request(h, [Unloading("Before", to: new TimeOnly(7, 0))]), CancellationToken.None);
        Assert.Equal(30m, result.Total);

        var loading = new StopTimeInput(false, "Before", null, new TimeOnly(7, 0), false, null);
        var applies = await h.Engine.CalculateAsync(Request(h, [loading]), CancellationToken.None);
        Assert.Equal(55m, applies.Total);
    }

    [Fact]
    public async Task TimeConditions_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateTimeOptionAsync(h, "VOOR10", "Levering vóór 10u", 35m,
            ServiceConditionKind.StopTimeBefore, new TimeOnly(10, 0));

        // A second tenant in the SAME database sees neither the option nor the condition.
        var otherTenantId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var otherUnitId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = otherTenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        h.Db.Context.UnitTypes.Add(new UnitType { Id = otherUnitId, TenantId = otherTenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var otherEngine = new PricingEngine(h.Db.Context, new DevTenantContext(otherTenantId));
        var result = await otherEngine.CalculateAsync(
            new PriceCalculationRequest(otherCustomerId, Today, [new PriceCalculationLineInput(otherUnitId, 3)],
                "BE", null, null, null, null, [],
                StopTimes: [Unloading("Before", to: new TimeOnly(7, 0))]), CancellationToken.None);

        Assert.Empty(result.ServiceLines);
        Assert.DoesNotContain(result.Lines, l => l.Label.Contains("Levering vóór 10u"));
    }
}
