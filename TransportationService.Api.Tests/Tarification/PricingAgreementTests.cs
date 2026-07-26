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

/// <summary>Pricing agreements (rate cards): CRUD, validation, rule grouping.</summary>
public class PricingAgreementTests
{
    private static readonly DateOnly Today = new(2026, 7, 25);

    private sealed record Harness(SqliteTestDbContext Db, PricingAdminService Admin, Guid TenantId, Guid CustomerId, Guid PalletUnitId);

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
        var admin = new PricingAdminService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, admin, tenantId, customerId, palletUnitId);
    }

    private static SavePricingAgreementRequest Agreement(
        Guid? customerId, string name = "Distributie België 2026-Q4",
        decimal? minimum = null, IReadOnlyList<SavePricingAgreementSurchargeRequest>? surcharges = null) =>
        new(customerId, name, Today.AddMonths(-1), null, true, minimum, "Historisch gunstig contract", surcharges);

    [Fact]
    public async Task Agreement_CrudRoundTrip_WithSurcharges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId, minimum: 60m,
            surcharges: [new SavePricingAgreementSurchargeRequest("Duurtoeslag", SurchargeKind.Percent, 5m)]), CancellationToken.None);
        Assert.Equal("Distributie België 2026-Q4", created.Name);
        Assert.Equal("Klant A", created.CustomerName);
        Assert.Equal(60m, created.MinimumAmount);
        Assert.Equal("Historisch gunstig contract", created.Notes);
        var surcharge = Assert.Single(created.Surcharges);
        Assert.Equal(SurchargeKind.Percent, surcharge.Kind);

        var updated = await h.Admin.UpdateAgreementAsync(created.Id, Agreement(h.CustomerId, "Distributie 2027") with
        {
            Surcharges = [new SavePricingAgreementSurchargeRequest("Vaste toeslag", SurchargeKind.Fixed, 12.5m)],
        }, CancellationToken.None);
        Assert.Equal("Distributie 2027", updated!.Name);
        Assert.Equal("Vaste toeslag", Assert.Single(updated.Surcharges).Name);

        Assert.True(await h.Admin.DeleteAgreementAsync(created.Id, CancellationToken.None));
        Assert.Empty(await h.Admin.ListAgreementsAsync(h.CustomerId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAgreementAsync_ReturnsSingleAgreement_OrNullWhenMissingOrForeignTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId), CancellationToken.None);

        var fetched = await h.Admin.GetAgreementAsync(created.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(created.Name, fetched!.Name);

        Assert.Null(await h.Admin.GetAgreementAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListAgreementsAsync_IncludeAll_ReturnsEveryAgreementRegardlessOfCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId, "Klant A tabel"), CancellationToken.None);
        await h.Admin.CreateAgreementAsync(Agreement(null, "Algemene tabel") with { IsShared = true }, CancellationToken.None);

        // Without includeAll, an unspecified customerId only returns the company-wide/shared ones.
        var companyWide = await h.Admin.ListAgreementsAsync(null, CancellationToken.None);
        Assert.Single(companyWide);
        Assert.Equal("Algemene tabel", companyWide[0].Name);

        var all = await h.Admin.ListAgreementsAsync(null, CancellationToken.None, includeAll: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.Name == "Klant A tabel");
        Assert.Contains(all, a => a.Name == "Algemene tabel");
    }

    [Fact]
    public async Task Agreement_Validation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            Agreement(h.CustomerId, name: "  "), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            Agreement(h.CustomerId) with { EffectiveUntil = Today.AddMonths(-2) }, CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            Agreement(h.CustomerId, minimum: -5m), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Admin.CreateAgreementAsync(
            Agreement(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Rule_PersistsAgreementPriorityBaseAmountAndOversize()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId), CancellationToken.None);

        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", Today.AddMonths(-1), null, true, 22m, 60m, null,
            AgreementId: agreement.Id, Priority: 5, BaseAmount: 10m,
            OversizeLengthCm: 125m, OversizeWidthCm: 85m, OversizeBillableFactor: 2m), CancellationToken.None);

        Assert.Equal(agreement.Id, rule.AgreementId);
        Assert.Equal("Distributie België 2026-Q4", rule.AgreementName);
        Assert.Equal(5, rule.Priority);
        Assert.Equal(10m, rule.BaseAmount);
        Assert.Equal(2m, rule.OversizeBillableFactor);
    }

    [Fact]
    public async Task ListRulesAsync_AgreementIdFilter_ReturnsOnlyThatAgreementsRules_RegardlessOfCustomerFilter()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreementX = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId, "Tabel X"), CancellationToken.None);
        var agreementY = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId, "Tabel Y"), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Regel X", Today.AddMonths(-1), null, true, 10m, null, null, AgreementId: agreementX.Id), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Regel Y", Today.AddMonths(-1), null, true, 20m, null, null, AgreementId: agreementY.Id), CancellationToken.None);

        var forX = await h.Admin.ListRulesAsync(h.CustomerId, CancellationToken.None, agreementId: agreementX.Id);
        Assert.Equal("Regel X", Assert.Single(forX).Name);
    }

    [Fact]
    public async Task Rule_OrderMeasureBases_NeedNoUnit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var km = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Kilometertarief", Today.AddMonths(-1), null, true, 1.2m, null, null, BaseAmount: 25m), CancellationToken.None);
        Assert.Equal(PriceRuleBasis.PerKm, km.Basis);
        Assert.Null(km.UnitTypeId);

        // A per-unit rule still requires a unit.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerUnit, null,
            "Zonder eenheid", Today.AddMonths(-1), null, true, 10m, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Rule_OversizeValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Factor without threshold and threshold without factor are both refused.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Fout", Today.AddMonths(-1), null, true, 22m, null, null,
            OversizeBillableFactor: 2m), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Fout", Today.AddMonths(-1), null, true, 22m, null, null,
            OversizeLengthCm: 125m), CancellationToken.None));
    }

    [Fact]
    public async Task Rule_InForeignCustomersAgreement_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = h.TenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        var agreement = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            otherCustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets B", Today.AddMonths(-1), null, true, 30m, null, null,
            AgreementId: agreement.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Agreement_WithRules_CannotBeDeleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", Today.AddMonths(-1), null, true, 22m, null, null,
            AgreementId: agreement.Id), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.DeleteAgreementAsync(agreement.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Agreements_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateAgreementAsync(Agreement(h.CustomerId), CancellationToken.None);

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignAdmin = new PricingAdminService(h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)));

        Assert.Empty(await foreignAdmin.ListAgreementsAsync(h.CustomerId, CancellationToken.None));
    }
}
