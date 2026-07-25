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
/// Customer service overrides (spec §7/9/19): the global admin configuration is the default,
/// a customer override replaces it (date-aware), a disabled service never charges, and the
/// effective source is visible to the pricing engine and the customer detail page.
/// </summary>
public class CustomerServiceOverrideTests
{
    private static readonly DateOnly Today = new(2026, 7, 25);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid Voor10Id);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);

        await admin.CreateRuleAsync(new SavePriceRuleRequest(
            customerId, palletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", Today.AddMonths(-1), null, true, 35m, null, null), CancellationToken.None);
        var voor10 = await admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "VOOR10", "Levering vóór 10:00", SurchargeKind.Fixed, 15m, true, 0), CancellationToken.None);
        return new Harness(db, engine, admin, tenantId, customerId, palletUnitId, voor10.Id);
    }

    private static PriceCalculationRequest Request(Harness h, DateOnly? date = null) =>
        new(h.CustomerId, date ?? Today, [new PriceCalculationLineInput(h.PalletUnitId, 2)],
            "BE", null, null, null, null, [h.Voor10Id]);

    private static SaveCustomerPricingConfigRequest ConfigWith(SaveCustomerOptionPriceRequest option) =>
        new([], [option]);

    [Fact]
    public async Task WithoutOverride_TheGlobalDefaultApplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);

        // 2 × 35 base + €15 global default.
        Assert.Equal(85m, result.Total);
        Assert.Contains(result.Lines, l => l.Label == "Levering vóór 10:00" && l.Amount == 15m && l.Source == "Algemene standaard");

        var config = await h.Admin.GetCustomerConfigAsync(h.CustomerId, CancellationToken.None);
        var service = Assert.Single(config!.ServiceOptions);
        Assert.Equal(15m, service.EffectiveValue);
        Assert.Equal("Algemene standaard", service.Source);
    }

    [Fact]
    public async Task Override_ReplacesTheGlobalValue_AndShowsItsSource()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            ConfigWith(new SaveCustomerOptionPriceRequest(h.Voor10Id, 10m)), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);

        Assert.Equal(80m, result.Total); // 70 + €10 customer override
        Assert.Contains(result.Lines, l => l.Label == "Levering vóór 10:00" && l.Amount == 10m && l.Source == "Klanttarief");

        var config = await h.Admin.GetCustomerConfigAsync(h.CustomerId, CancellationToken.None);
        var service = Assert.Single(config!.ServiceOptions);
        Assert.Equal(10m, service.EffectiveValue);
        Assert.Equal(15m, service.DefaultValue);
        Assert.Equal("Klanttarief", service.Source);
    }

    [Fact]
    public async Task DisabledService_IsNotCharged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            ConfigWith(new SaveCustomerOptionPriceRequest(h.Voor10Id, null, Disabled: true)), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);

        Assert.Equal(70m, result.Total); // base only — the disabled service never charges
        Assert.Empty(result.ServiceLines);
        Assert.Contains(result.Lines, l => l.Label.Contains("uitgeschakeld voor deze klant") && l.Informational);
    }

    [Fact]
    public async Task OverrideDates_AreRespected_ByTariffDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // €10 override only from 1 October.
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            ConfigWith(new SaveCustomerOptionPriceRequest(h.Voor10Id, 10m, EffectiveFrom: new DateOnly(2026, 10, 1))),
            CancellationToken.None);

        var before = await h.Engine.CalculateAsync(Request(h, new DateOnly(2026, 9, 15)), CancellationToken.None);
        var after = await h.Engine.CalculateAsync(Request(h, new DateOnly(2026, 10, 15)), CancellationToken.None);

        Assert.Contains(before.Lines, l => l.Label == "Levering vóór 10:00" && l.Amount == 15m);
        Assert.Contains(after.Lines, l => l.Label == "Levering vóór 10:00" && l.Amount == 10m);
    }

    [Fact]
    public async Task Reset_RemovesTheOverrideRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            ConfigWith(new SaveCustomerOptionPriceRequest(h.Voor10Id, 10m)), CancellationToken.None);

        // "Algemene waarde opnieuw gebruiken": everything back to null/off drops the row.
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            ConfigWith(new SaveCustomerOptionPriceRequest(h.Voor10Id, null)), CancellationToken.None);

        var config = await h.Admin.GetCustomerConfigAsync(h.CustomerId, CancellationToken.None);
        var service = Assert.Single(config!.ServiceOptions);
        Assert.Null(service.CustomerValue);
        Assert.Equal("Algemene standaard", service.Source);
        Assert.Equal(15m, service.EffectiveValue);
    }

    [Fact]
    public async Task QuantityBasedServices_MultiplyByEnteredQuantity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var wachttijd = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "WACHT", "Wachttijd", SurchargeKind.PerHour, 45m, true, 1), CancellationToken.None);
        var extraStop = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "STOP", "Extra stop", SurchargeKind.PerStop, 20m, true, 2), CancellationToken.None);

        var request = new PriceCalculationRequest(h.CustomerId, Today,
            [new PriceCalculationLineInput(h.PalletUnitId, 2)], "BE", null, null, null, null, [],
            Services:
            [
                new PriceServiceInput(wachttijd.Id, 3m),
                new PriceServiceInput(extraStop.Id, 2m),
                new PriceServiceInput(h.Voor10Id),
            ]);
        var result = await h.Engine.CalculateAsync(request, CancellationToken.None);

        // 70 base + 3 × 45 + 2 × 20 + 15 = 260.
        Assert.Equal(260m, result.Total);
        Assert.Contains(result.Lines, l => l.Label == "Wachttijd (3 uur)" && l.Amount == 135m);
        Assert.Contains(result.Lines, l => l.Label == "Extra stop (2 stops)" && l.Amount == 40m);
        var wachtLine = result.ServiceLines.Single(s => s.ServiceOptionId == wachttijd.Id);
        Assert.Equal(3m, wachtLine.Quantity);

        // Without a quantity the service explains itself instead of charging.
        var withoutQuantity = await h.Engine.CalculateAsync(request with
        {
            Services = [new PriceServiceInput(wachttijd.Id)],
        }, CancellationToken.None);
        Assert.Equal(70m, withoutQuantity.Total);
        Assert.Contains(withoutQuantity.Lines, l => l.Label.Contains("geef het aantal uur op") && l.Informational);
    }

    [Fact]
    public async Task CustomerMinimum_LiftsTheServiceAmount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            ConfigWith(new SaveCustomerOptionPriceRequest(h.Voor10Id, 5m, MinimumAmount: 12m)), CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);

        Assert.Contains(result.Lines, l => l.Label == "Levering vóór 10:00" && l.Amount == 12m);
    }
}
