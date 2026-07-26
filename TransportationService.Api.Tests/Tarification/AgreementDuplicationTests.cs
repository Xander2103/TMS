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
/// Duplicating a pricing agreement as a new version: full copy of rules/brackets/surcharges,
/// optional adjustment, closing the source window, assignments intentionally not copied, and
/// tenant isolation.
/// </summary>
public class AgreementDuplicationTests
{
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
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        return new Harness(db, admin, tenantId, customerId, palletUnitId);
    }

    /// <summary>A shared table with one bracket rule and a customer assignment (to prove assignments are NOT copied).</summary>
    private static async Task<PricingAgreementDto> SeedSharedAgreementWithRuleAsync(Harness h, DateOnly effectiveFrom)
    {
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Distributie België 2026", effectiveFrom, null, true, 60m, "Historisch gunstig contract",
            [new SavePricingAgreementSurchargeRequest("Duurtoeslag", SurchargeKind.Percent, 5m)],
            IsShared: true), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Europallet Brussel", effectiveFrom, null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null),
                new SavePriceRuleBracketRequest(2, null, 80m, 20m),
            ],
            agreement.Id), CancellationToken.None);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerId, -5m, null, null, null, null)], CancellationToken.None);
        return agreement;
    }

    [Fact]
    public async Task Duplicate_CopiesRulesBracketsSurcharges_AppliesAdjustment_ButNotAssignments()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var source = await SeedSharedAgreementWithRuleAsync(h, new DateOnly(2026, 1, 1));

        var duplicated = await h.Admin.DuplicateAgreementAsync(source.Id,
            new DuplicateAgreementRequest("Distributie België 2027", new DateOnly(2027, 1, 1), false, Percent: 3.5m),
            CancellationToken.None);

        Assert.NotNull(duplicated);
        Assert.Equal("Distributie België 2027", duplicated!.Name);
        Assert.True(duplicated.IsShared);
        Assert.Equal(60m, duplicated.MinimumAmount);
        Assert.Single(duplicated.Surcharges);
        Assert.Equal("Duurtoeslag", duplicated.Surcharges[0].Name);
        // Assignments are NOT copied — a fresh version starts unassigned.
        Assert.Equal(0, duplicated.CustomerCount);
        var duplicatedAssignments = await h.Admin.ListAssignmentsAsync(duplicated.Id, CancellationToken.None);
        Assert.Empty(duplicatedAssignments!);

        var newRule = await h.Db.Context.PriceRules.Include(r => r.Brackets)
            .SingleAsync(r => r.TenantId == h.TenantId && r.AgreementId == duplicated.Id);
        Assert.Equal("Europallet Brussel", newRule.Name);
        Assert.Equal(new DateOnly(2027, 1, 1), newRule.EffectiveFrom);
        Assert.Null(newRule.EffectiveUntil);
        var brackets = newRule.Brackets.OrderBy(b => b.FromQuantity).ToList();
        Assert.Equal(2, brackets.Count);
        Assert.Equal(51.75m, brackets[0].Price); // 50 × 1.035
        Assert.Equal(82.80m, brackets[1].Price); // 80 × 1.035
        Assert.Equal(20.70m, brackets[1].PricePerExtraUnit); // 20 × 1.035

        // Source untouched (CloseSource = false).
        var sourceRule = await h.Db.Context.PriceRules
            .SingleAsync(r => r.TenantId == h.TenantId && r.Id != newRule.Id && r.Name == "Europallet Brussel");
        Assert.Null(sourceRule.EffectiveUntil);

        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(),
            l => l.EntityType == "PricingAgreement" && l.Action == "duplicated");
    }

    [Fact]
    public async Task Duplicate_CloseSource_ClosesSourceWindowsAndValidatesDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var source = await SeedSharedAgreementWithRuleAsync(h, new DateOnly(2026, 1, 1));

        // EffectiveFrom must be strictly after the source's EffectiveFrom when closing it.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.DuplicateAgreementAsync(source.Id,
            new DuplicateAgreementRequest("Distributie België 2026 v2", new DateOnly(2026, 1, 1), true, Percent: 3.5m),
            CancellationToken.None));

        var duplicated = await h.Admin.DuplicateAgreementAsync(source.Id,
            new DuplicateAgreementRequest("Distributie België 2027", new DateOnly(2027, 1, 1), true, Percent: 3.5m),
            CancellationToken.None);
        Assert.NotNull(duplicated);

        var sourceAgreement = await h.Db.Context.PricingAgreements.SingleAsync(a => a.Id == source.Id);
        Assert.Equal(new DateOnly(2026, 12, 31), sourceAgreement.EffectiveUntil);

        var sourceRule = await h.Db.Context.PriceRules.SingleAsync(r => r.TenantId == h.TenantId && r.AgreementId == source.Id);
        Assert.Equal(new DateOnly(2026, 12, 31), sourceRule.EffectiveUntil);
    }

    [Fact]
    public async Task Duplicate_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var source = await SeedSharedAgreementWithRuleAsync(h, new DateOnly(2026, 1, 1));

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignAdmin = new PricingAdminService(h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)));

        var result = await foreignAdmin.DuplicateAgreementAsync(source.Id,
            new DuplicateAgreementRequest("Should not work", new DateOnly(2027, 1, 1), false), CancellationToken.None);

        Assert.Null(result);
    }
}
