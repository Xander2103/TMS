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
/// Customer-wide bracket-override read model (GET api/customers/{id}/bracket-overrides): every
/// "klantafwijking" of one customer across all rules with the CURRENT standard price of the
/// targeted row — the customer detail's "Staffelafwijkingen" block. Pure read model; the
/// effective application stays in PricingEngine (BracketOverrideTests).
/// </summary>
public class CustomerBracketOverridesReadModelTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Harness(SqliteTestDbContext Db, PricingAdminService Admin, Guid TenantId, Guid CustomerAId, Guid CustomerBId, Guid PalletUnitId);

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
        var admin = new PricingAdminService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, admin, tenantId, customerAId, customerBId, palletUnitId);
    }

    /// <summary>Shared table + three-tier bracket rule (1=€50, 2=€80, 3+=€105), like the spec S3 example.</summary>
    private static async Task<(PricingAgreementDto Agreement, PriceRuleDto Rule)> SeedSharedBracketRuleAsync(Harness h)
    {
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Distributie België 2026", Today.AddMonths(-1), null, true, null, null, null, IsShared: true), CancellationToken.None);
        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets gedeeld", Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null),
                new SavePriceRuleBracketRequest(2, 2, 80m, null),
                new SavePriceRuleBracketRequest(3, null, 105m, null),
            ], AgreementId: agreement.Id), CancellationToken.None);
        return (agreement, rule);
    }

    [Fact]
    public async Task ListsOnlyThisCustomersOverrides_WithStandardPriceAndContext()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreement, rule) = await SeedSharedBracketRuleAsync(h);
        await h.Admin.CreateBracketOverrideAsync(rule.Id,
            new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 2, 2, Price: 72m), CancellationToken.None);
        await h.Admin.CreateBracketOverrideAsync(rule.Id,
            new SavePriceRuleBracketOverrideRequest(h.CustomerBId, 3, null, Price: 99m), CancellationToken.None);

        var rows = await h.Admin.ListCustomerBracketOverridesAsync(h.CustomerAId, CancellationToken.None);

        var row = Assert.Single(rows!);
        Assert.Equal(rule.Id, row.PriceRuleId);
        Assert.Equal("Pallets gedeeld", row.RuleName);
        Assert.Equal(agreement.Id, row.AgreementId);
        Assert.Equal("Distributie België 2026", row.AgreementName);
        Assert.Equal("Europallet", row.UnitTypeName);
        Assert.Equal(2m, row.FromQuantity);
        // Standard price comes from the CURRENT bracket row; the override price sits beside it.
        Assert.Equal(80m, row.StandardPrice);
        Assert.Equal(72m, row.Price);
        Assert.False(row.Orphaned);
    }

    [Fact]
    public async Task CustomerWithoutOverrides_ReturnsEmpty_AndUnknownCustomerNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (_, rule) = await SeedSharedBracketRuleAsync(h);
        await h.Admin.CreateBracketOverrideAsync(rule.Id,
            new SavePriceRuleBracketOverrideRequest(h.CustomerBId, 2, 2, Price: 72m), CancellationToken.None);

        Assert.Empty((await h.Admin.ListCustomerBracketOverridesAsync(h.CustomerAId, CancellationToken.None))!);
        Assert.Null(await h.Admin.ListCustomerBracketOverridesAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RowThatNoLongerExists_IsReportedOrphaned_WithoutStandardPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (agreement, rule) = await SeedSharedBracketRuleAsync(h);
        await h.Admin.CreateBracketOverrideAsync(rule.Id,
            new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 2, 2, Price: 72m), CancellationToken.None);
        // Rule edit replaces the rows wholesale; the 2-2 row disappears → the override is orphaned.
        await h.Admin.UpdateRuleAsync(rule.Id, new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets gedeeld", Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 4, 60m, null),
                new SavePriceRuleBracketRequest(5, null, 110m, null),
            ], AgreementId: agreement.Id), CancellationToken.None);

        var row = Assert.Single((await h.Admin.ListCustomerBracketOverridesAsync(h.CustomerAId, CancellationToken.None))!);
        Assert.True(row.Orphaned);
        Assert.Null(row.StandardPrice);
        Assert.Equal(72m, row.Price);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantResolvesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (_, rule) = await SeedSharedBracketRuleAsync(h);
        await h.Admin.CreateBracketOverrideAsync(rule.Id,
            new SavePriceRuleBracketOverrideRequest(h.CustomerAId, 2, 2, Price: 72m), CancellationToken.None);

        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var otherTenant = new DevTenantContext(otherTenantId);
        var otherAdmin = new PricingAdminService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)));

        // Tenant B cannot even resolve tenant A's customer id — the endpoint 404s.
        Assert.Null(await otherAdmin.ListCustomerBracketOverridesAsync(h.CustomerAId, CancellationToken.None));
    }
}
