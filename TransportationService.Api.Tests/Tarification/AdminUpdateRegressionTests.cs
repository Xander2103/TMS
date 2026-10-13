using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>
/// Regression: full-replace updates of child collections (rule brackets, zone areas) crashed with
/// DbUpdateConcurrencyException — children created with client-set Guid keys and reached via a
/// navigation of a tracked parent were tracked as Modified (an UPDATE against a non-existent row)
/// instead of Added. ApplyAgreement/ApplyDiscount already marked children Added explicitly;
/// ApplyRule and ApplyZone did not, so every bracket or postcode-area edit through the API failed.
/// </summary>
public class AdminUpdateRegressionTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private sealed record Harness(SqliteTestDbContext Db, PricingAdminService Admin, Guid TenantId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var admin = new PricingAdminService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, admin, tenantId, palletUnitId);
    }

    [Fact]
    public async Task UpdateRule_ReplacingBracketRows_Persists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Staffel", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(1, 1, 50m, null), new SavePriceRuleBracketRequest(2, null, 80m, null)]),
            CancellationToken.None);

        var updated = await h.Admin.UpdateRuleAsync(rule.Id, new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Staffel", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(1, 2, 55m, null), new SavePriceRuleBracketRequest(3, null, 85m, null)]),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(2, updated.Brackets.Count);
        Assert.Equal(55m, updated.Brackets[0].Price);

        // Reload from the store: exactly the two new rows survive.
        var fresh = await h.Admin.ListRulesAsync(null, CancellationToken.None);
        var brackets = Assert.Single(fresh, r => r.Id == rule.Id).Brackets;
        Assert.Equal(2, brackets.Count);
        Assert.Equal([55m, 85m], brackets.OrderBy(b => b.FromQuantity).Select(b => b.Price).ToArray());
    }

    [Fact]
    public async Task UpdateZone_ReplacingAreas_Persists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zone = await h.Admin.CreateZoneAsync(new SavePricingZoneRequest(
            "BE1", "Zone 1", true, 0,
            [new SavePricingZoneAreaRequest("BE", "1000", "1999")]), CancellationToken.None);

        var updated = await h.Admin.UpdateZoneAsync(zone.Id, new SavePricingZoneRequest(
            "BE1", "Zone 1", true, 0,
            [new SavePricingZoneAreaRequest("BE", "2000", "2999"), new SavePricingZoneAreaRequest("BE", "3000", "3999")]),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(2, updated.Areas.Count);

        var fresh = (await h.Admin.ListZonesAsync(CancellationToken.None)).Single(z => z.Id == zone.Id);
        Assert.Equal(["2000", "3000"], fresh.Areas.OrderBy(a => a.PostalCodeFrom).Select(a => a.PostalCodeFrom).ToArray());
    }
}
