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
/// Warehouse/logistics service charges (Picking, PAL UIT, Administratie, ...): calculation bases
/// beyond hour/stop (per unit, per order line, per kg/m³/ldm, per day/pallet-day), auto-apply
/// (contract services quantified from the order without manual selection) and the ADR condition.
/// </summary>
public class WarehouseServiceTests
{
    private static readonly DateOnly Today = new(2026, 7, 26);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid ColliUnitId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var colliUnitId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant X", CustomerNumber = "KL-1", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = colliUnitId, TenantId = tenantId, Code = "COLLI", Name = "colli", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "pallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerId, colliUnitId, palletUnitId);
    }

    private static Task<ServiceOptionDto> CreateOptionAsync(
        Harness h, string code, string name, SurchargeKind kind, decimal value,
        Guid? unitTypeId = null, bool autoApply = false, bool onlyForAdr = false) =>
        h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            code, name, kind, value, true, 0,
            UnitTypeId: unitTypeId, AutoApply: autoApply, OnlyForAdr: onlyForAdr), CancellationToken.None);

    private static PriceCalculationRequest Request(
        Harness h,
        IReadOnlyList<PriceCalculationLineInput>? lines = null,
        IReadOnlyList<PriceServiceInput>? services = null,
        bool? adrRequired = null,
        int? cargoLineCount = null,
        decimal? weightKg = null,
        decimal? volumeM3 = null,
        decimal? loadingMeters = null) =>
        new(h.CustomerId, Today, lines ?? [], "BE", null, weightKg, null, null, [],
            Services: services, VolumeM3: volumeM3, LoadingMeters: loadingMeters,
            AdrRequired: adrRequired, CargoLineCount: cargoLineCount);

    // --- S7/S8: PerUnit auto-apply, derived quantity from matching order line ------------------

    [Fact]
    public async Task PerUnit_AutoApplies_WithQuantityDerivedFromMatchingLine_Colli()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, h.ColliUnitId, autoApply: true);

        var result = await h.Engine.CalculateAsync(
            Request(h, lines: [new PriceCalculationLineInput(h.ColliUnitId, 3)]), CancellationToken.None);

        var line = Assert.Single(result.ServiceLines);
        Assert.True(line.AutoApplied);
        Assert.Equal(3.75m, line.Amount);
        Assert.Contains(result.Lines, l => l.Label == "Picking (3 colli)" && l.Amount == 3.75m && l.Source == "Automatisch (contract)");
    }

    [Fact]
    public async Task PerUnit_AutoApplies_WithQuantityDerivedFromMatchingLine_Pallet()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "PALUIT", "PAL UIT", SurchargeKind.PerUnit, 4.50m, h.PalletUnitId, autoApply: true);

        var result = await h.Engine.CalculateAsync(
            Request(h, lines: [new PriceCalculationLineInput(h.PalletUnitId, 5)]), CancellationToken.None);

        var line = Assert.Single(result.ServiceLines);
        Assert.Equal(22.50m, line.Amount);
        Assert.Contains(result.Lines, l => l.Label == "PAL UIT (5 pallet)" && l.Amount == 22.50m);
    }

    // --- S9: multiple auto-applied services combine -------------------------------------------

    [Fact]
    public async Task MultipleAutoAppliedServices_EachContributeTheirOwnLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "ADMIN", "Administratie", SurchargeKind.PerOrderLine, 1.50m, autoApply: true);
        await CreateOptionAsync(h, "PALUIT", "PAL UIT", SurchargeKind.PerUnit, 5m, h.PalletUnitId, autoApply: true);
        await CreateOptionAsync(h, "ADMFEE", "Administratiekost", SurchargeKind.Fixed, 3m, autoApply: true);

        var result = await h.Engine.CalculateAsync(
            Request(h, lines: [new PriceCalculationLineInput(h.PalletUnitId, 4)], cargoLineCount: 3), CancellationToken.None);

        Assert.Contains(result.ServiceLines, s => s.Name == "Administratie" && s.Amount == 4.50m);
        Assert.Contains(result.ServiceLines, s => s.Name == "PAL UIT" && s.Amount == 20m);
        Assert.Contains(result.ServiceLines, s => s.Name == "Administratiekost" && s.Amount == 3m);
        Assert.Equal(27.50m, result.Total); // 4.50 + 20 + 3
    }

    // --- OnlyForAdr ------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyForAdr_ChargesWhenAdrRequired_ElseInformational()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var adrFee = await CreateOptionAsync(h, "ADRFEE", "ADR-toeslag", SurchargeKind.Fixed, 10m, onlyForAdr: true);

        var withAdr = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(adrFee.Id)], adrRequired: true), CancellationToken.None);
        Assert.Equal(10m, withAdr.Total);
        Assert.Contains(withAdr.ServiceLines, s => s.Name == "ADR-toeslag" && s.Amount == 10m);

        var withoutAdr = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(adrFee.Id)], adrRequired: false), CancellationToken.None);
        Assert.Equal(0m, withoutAdr.Total);
        Assert.Empty(withoutAdr.ServiceLines);
        Assert.Contains(withoutAdr.Lines, l => l.Label.Contains("alleen van toepassing bij ADR") && l.Informational);

        var nullAdr = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(adrFee.Id)]), CancellationToken.None);
        Assert.Equal(0m, nullAdr.Total);
    }

    [Fact]
    public async Task OnlyForAdr_AutoApply_OnlyEngagesOnAdrOrders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "ADRFEE", "ADR-toeslag", SurchargeKind.Fixed, 10m, autoApply: true, onlyForAdr: true);

        var withAdr = await h.Engine.CalculateAsync(Request(h, adrRequired: true), CancellationToken.None);
        Assert.Equal(10m, withAdr.Total);

        var withoutAdr = await h.Engine.CalculateAsync(Request(h, adrRequired: false), CancellationToken.None);
        Assert.Equal(0m, withoutAdr.Total);
        Assert.Empty(withoutAdr.ServiceLines);
        // Never selected and never ADR-eligible: no line at all (not even informational spam).
        Assert.DoesNotContain(withoutAdr.Lines, l => l.Label.Contains("ADR-toeslag"));
    }

    // --- AutoApplyOverride -----------------------------------------------------------------------

    [Fact]
    public async Task AutoApplyOverride_False_SuppressesAGloballyAutoService()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var option = await CreateOptionAsync(h, "ADMFEE", "Administratiekost", SurchargeKind.Fixed, 5m, autoApply: true);

        var before = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(5m, before.Total);

        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            new SaveCustomerPricingConfigRequest([], [new SaveCustomerOptionPriceRequest(option.Id, null, AutoApplyOverride: false)]),
            CancellationToken.None);

        var after = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(0m, after.Total);
        Assert.Empty(after.ServiceLines);
    }

    [Fact]
    public async Task AutoApplyOverride_True_EnablesANonAutoGlobalServiceForThisCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var option = await CreateOptionAsync(h, "ADMFEE", "Administratiekost", SurchargeKind.Fixed, 5m, autoApply: false);

        var before = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(0m, before.Total);

        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            new SaveCustomerPricingConfigRequest([], [new SaveCustomerOptionPriceRequest(option.Id, null, AutoApplyOverride: true)]),
            CancellationToken.None);

        var after = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(5m, after.Total);
        Assert.Single(after.ServiceLines);
        Assert.Equal("Automatisch (contract)", after.Lines.Single(l => l.Label == "Administratiekost").Source);
    }

    // --- Disabled + AutoApply combined -----------------------------------------------------------

    [Fact]
    public async Task DisabledForCustomer_NeverAutoApplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var option = await CreateOptionAsync(h, "ADMFEE", "Administratiekost", SurchargeKind.Fixed, 5m, autoApply: true);
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId,
            new SaveCustomerPricingConfigRequest([], [new SaveCustomerOptionPriceRequest(option.Id, null, Disabled: true)]),
            CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);

        Assert.Equal(0m, result.Total);
        Assert.Empty(result.ServiceLines);
        Assert.DoesNotContain(result.Lines, l => l.Label.Contains("Administratiekost"));
    }

    // --- Entered quantity beats derived quantity --------------------------------------------------

    [Fact]
    public async Task EnteredQuantity_BeatsDerivedQuantity_ForPerUnit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var picking = await CreateOptionAsync(h, "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, h.ColliUnitId, autoApply: true);

        var result = await h.Engine.CalculateAsync(
            Request(h,
                lines: [new PriceCalculationLineInput(h.ColliUnitId, 8)],
                services: [new PriceServiceInput(picking.Id, 6m)]),
            CancellationToken.None);

        var line = Assert.Single(result.ServiceLines);
        Assert.Equal(6m, line.Quantity);
        Assert.Equal(7.50m, line.Amount);
    }

    // --- PerKg / PerM3 / PerLdm --------------------------------------------------------------------

    [Fact]
    public async Task PerKg_AppliesFromWeight_MissingWeight_IsInformational()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "KOEL", "Koeltoeslag", SurchargeKind.PerKg, 0.10m, autoApply: true);

        var withWeight = await h.Engine.CalculateAsync(Request(h, weightKg: 250m), CancellationToken.None);
        Assert.Equal(25m, withWeight.Total);
        Assert.Contains(withWeight.Lines, l => l.Label == "Koeltoeslag (250 kg)" && l.Amount == 25m);

        var withoutWeight = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(0m, withoutWeight.Total);
        Assert.Empty(withoutWeight.ServiceLines);
        Assert.Contains(withoutWeight.Lines, l => l.Label.Contains("geen gewicht gekend") && l.Informational);
    }

    [Fact]
    public async Task PerM3_AppliesFromVolume_MissingVolume_IsInformational()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "VOLU", "Volumetoeslag", SurchargeKind.PerM3, 2m, autoApply: true);

        var withVolume = await h.Engine.CalculateAsync(Request(h, volumeM3: 10m), CancellationToken.None);
        Assert.Equal(20m, withVolume.Total);
        Assert.Contains(withVolume.Lines, l => l.Label == "Volumetoeslag (10 m³)" && l.Amount == 20m);

        var withoutVolume = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(0m, withoutVolume.Total);
        Assert.Contains(withoutVolume.Lines, l => l.Label.Contains("geen volume gekend") && l.Informational);
    }

    [Fact]
    public async Task PerLdm_AppliesFromLoadingMeters_MissingLoadingMeters_IsInformational()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "LDM", "Laadmetertoeslag", SurchargeKind.PerLdm, 3m, autoApply: true);

        var withLdm = await h.Engine.CalculateAsync(Request(h, loadingMeters: 4m), CancellationToken.None);
        Assert.Equal(12m, withLdm.Total);
        Assert.Contains(withLdm.Lines, l => l.Label == "Laadmetertoeslag (4 ldm)" && l.Amount == 12m);

        var withoutLdm = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(0m, withoutLdm.Total);
        Assert.Contains(withoutLdm.Lines, l => l.Label.Contains("geen laadmeters gekend") && l.Informational);
    }

    // --- PerOrderLine -----------------------------------------------------------------------------

    [Fact]
    public async Task PerOrderLine_AppliesFromCargoLineCount_MissingCount_IsInformational()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "ADMIN", "Administratie", SurchargeKind.PerOrderLine, 1.50m, autoApply: true);

        var withCount = await h.Engine.CalculateAsync(Request(h, cargoLineCount: 3), CancellationToken.None);
        Assert.Equal(4.50m, withCount.Total);

        var withoutCount = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(0m, withoutCount.Total);
        Assert.Contains(withoutCount.Lines, l => l.Label.Contains("aantal orderlijnen onbekend") && l.Informational);
    }

    // --- PerDay / PerPalletDay ----------------------------------------------------------------------

    [Fact]
    public async Task PerDay_RequiresEnteredQuantity_ThenMultiplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var opslag = await CreateOptionAsync(h, "OPSLAG", "Opslag", SurchargeKind.PerDay, 2m);

        var withoutQuantity = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(opslag.Id)]), CancellationToken.None);
        Assert.Equal(0m, withoutQuantity.Total);
        Assert.Contains(withoutQuantity.Lines, l => l.Label.Contains("geef het aantal dagen op") && l.Informational);

        var withQuantity = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(opslag.Id, 3m)]), CancellationToken.None);
        Assert.Equal(6m, withQuantity.Total);
        Assert.Contains(withQuantity.Lines, l => l.Label == "Opslag (3 dagen)" && l.Amount == 6m);
    }

    [Fact]
    public async Task PerPalletDay_RequiresEnteredQuantity_ThenMultiplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var opslag = await CreateOptionAsync(h, "PALOPSLAG", "Palletopslag", SurchargeKind.PerPalletDay, 1.5m);

        var withoutQuantity = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(opslag.Id)]), CancellationToken.None);
        Assert.Contains(withoutQuantity.Lines, l => l.Label.Contains("geef het aantal pallet-dagen op") && l.Informational);

        var withQuantity = await h.Engine.CalculateAsync(
            Request(h, services: [new PriceServiceInput(opslag.Id, 4m)]), CancellationToken.None);
        Assert.Equal(6m, withQuantity.Total); // 4 × 1.5
    }

    // --- Validation --------------------------------------------------------------------------------

    [Fact]
    public async Task Validation_PerUnitWithoutUnitType_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Validation_UnitTypeOnNonPerUnitKind_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("FIX", "Vast bedrag", SurchargeKind.Fixed, 5m, true, 0, UnitTypeId: h.ColliUnitId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Validation_AgreementSurcharge_WithPerUnitKind_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(h.CustomerId, "Afspraak", Today, null, true, null, null,
                [new SavePricingAgreementSurchargeRequest("Picking", SurchargeKind.PerUnit, 1.25m)]),
            CancellationToken.None));
    }

    // --- Tenant isolation ----------------------------------------------------------------------------

    [Fact]
    public async Task AutoApply_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateOptionAsync(h, "ADMFEE", "Administratiekost", SurchargeKind.Fixed, 5m, autoApply: true);

        var otherTenantId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = otherTenantId, Name = "Klant Y", CustomerNumber = "KL-2", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        var otherEngine = new PricingEngine(h.Db.Context, new DevTenantContext(otherTenantId));

        var result = await otherEngine.CalculateAsync(
            new PriceCalculationRequest(otherCustomerId, Today, [], "BE", null, null, null, null, []),
            CancellationToken.None);

        Assert.Equal(0m, result.Total);
        Assert.Empty(result.ServiceLines);
    }
}
