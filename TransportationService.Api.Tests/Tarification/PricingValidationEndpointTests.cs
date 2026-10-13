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
/// "Controle": PricingAdminService.ValidateAgreementConfigurationAsync + GET
/// /api/pricing/agreements/{id}/validate — every configuration-health check fires on crafted data,
/// a clean agreement returns an empty list, and the whole thing is tenant-isolated. Never throws.
/// </summary>
public class PricingValidationEndpointTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingAdminService Admin, Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid ZoneId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.PricingZones.Add(new PricingZone { Id = zoneId, TenantId = tenantId, Code = "Z1", Name = "Zone 1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        return new Harness(db, admin, tenantId, customerId, palletUnitId, zoneId);
    }

    private static Task<PricingAgreementDto> CreateAgreementAsync(
        Harness h, string name, bool isShared = false, Guid? customerId = null,
        Guid? baseAgreementId = null, decimal? minimumAmount = null, decimal? maximumAmount = null,
        DateOnly? effectiveFrom = null, DateOnly? effectiveUntil = null) =>
        h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            customerId, name, effectiveFrom ?? Today.AddMonths(-2), effectiveUntil, true,
            minimumAmount, null, null,
            IsShared: isShared, MaximumAmount: maximumAmount, BaseAgreementId: baseAgreementId), CancellationToken.None);

    private static Task<PriceRuleDto> CreateRuleAsync(
        Harness h, Guid agreementId, decimal unitPrice, string name = "Pallets",
        DateOnly? from = null, DateOnly? until = null, Guid? unitTypeId = null, Guid? zoneId = null, int priority = 0) =>
        h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, unitTypeId ?? h.PalletUnitId, PriceRuleBasis.PerUnit, zoneId,
            name, from ?? Today.AddMonths(-2), until, true, unitPrice, null, null,
            AgreementId: agreementId, Priority: priority), CancellationToken.None);

    // --- Clean agreement --------------------------------------------------------------------

    [Fact]
    public async Task CleanAgreement_ReturnsEmptyList()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Schone tabel");
        await CreateRuleAsync(h, agreement.Id, 30m);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Empty(checks);
    }

    [Fact]
    public async Task UnknownAgreement_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(checks);
    }

    // --- 1. Overlapping rule windows at identical specificity -> error -----------------------

    [Fact]
    public async Task OverlappingRuleWindows_SameSpecificity_ProducesError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Overlappende regels");
        await CreateRuleAsync(h, agreement.Id, 30m, "Regel A"); // open-ended from 2 months ago
        await CreateRuleAsync(h, agreement.Id, 35m, "Regel B"); // same unit/zone/basis/customer/priority — overlaps A

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "error"
            && c.Message.Contains("Regel A") && c.Message.Contains("Regel B") && c.Message.Contains("overlappen"));
    }

    [Fact]
    public async Task NonOverlappingRuleWindows_ProducesNoError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Opeenvolgende versies");
        await CreateRuleAsync(h, agreement.Id, 30m, "Regel v1", until: Today.AddDays(-1));
        await CreateRuleAsync(h, agreement.Id, 35m, "Regel v2", from: Today);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.DoesNotContain(checks!, c => c.Severity == "error");
    }

    // --- 2 & 3. Bracket gaps + brackets not starting at 0/1 -> warning ------------------------

    [Fact]
    public async Task BracketGap_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Staffeltabel met gat");
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Staffelregel", Today.AddMonths(-2), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 2, 50m, null),
                new SavePriceRuleBracketRequest(4, 5, 90m, null), // gap between 2 and 4
            ],
            AgreementId: agreement.Id), CancellationToken.None);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Staffelregel") && c.Message.Contains("gat") && c.Message.Contains("2") && c.Message.Contains("4"));
    }

    [Fact]
    public async Task BracketNotStartingAtZeroOrOne_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Staffeltabel met vreemde start");
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Laat-startende staffel", Today.AddMonths(-2), null, true, null, null,
            [new SavePriceRuleBracketRequest(2, null, 90m, null)],
            AgreementId: agreement.Id), CancellationToken.None);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Laat-startende staffel") && c.Message.Contains("0 of 1"));
    }

    [Fact]
    public async Task BracketOpenEndedBeforeLastRow_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Staffeltabel met vroegtijdig open einde");
        // Bypasses PricingAdminService's save-time overlap check (which would reject two brackets
        // sharing the same open-ended quantity range) — a data-drift shape where a NON-LAST
        // bracket's ToQuantity is null. That previously silently skipped the gap check for the
        // row after it (ordered[i - 1].ToQuantity is { } previousTo && ... only ran when the
        // previous row had a ToQuantity), so a real gap between rows 1 and 2 went unreported.
        h.Db.Context.PriceRules.Add(new PriceRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, AgreementId = agreement.Id, UnitTypeId = h.PalletUnitId,
            Basis = PriceRuleBasis.QuantityBracket, Name = "Staffelregel",
            EffectiveFrom = Today.AddMonths(-2), IsActive = true,
            Brackets =
            [
                new PriceRuleBracket { Id = Guid.NewGuid(), TenantId = h.TenantId, FromQuantity = 1, ToQuantity = null, Price = 50m },
                new PriceRuleBracket { Id = Guid.NewGuid(), TenantId = h.TenantId, FromQuantity = 5, ToQuantity = 10, Price = 90m },
            ],
        });
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Staffelregel") && c.Message.Contains("open einde"));
    }

    [Fact]
    public async Task GaplessBracketsStartingAtOne_ProducesNoBracketWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Nette staffeltabel");
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Nette staffel", Today.AddMonths(-2), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 2, 50m, null),
                new SavePriceRuleBracketRequest(3, null, 90m, null),
            ],
            AgreementId: agreement.Id), CancellationToken.None);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.DoesNotContain(checks!, c => c.Message.Contains("gat") || c.Message.Contains("0 of 1"));
    }

    // --- 4. Derived chain: base inactive / window mismatch (warning), cycle/depth drift (error) -

    [Fact]
    public async Task DerivedChain_InactiveBase_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var baseAgreement = await CreateAgreementAsync(h, "Basistabel", isShared: true);
        await CreateRuleAsync(h, baseAgreement.Id, 50m);
        var derived = await CreateAgreementAsync(h, "Afgeleide tabel", isShared: true, baseAgreementId: baseAgreement.Id);

        var tracked = await h.Db.Context.PricingAgreements.FirstAsync(a => a.Id == baseAgreement.Id);
        tracked.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(derived.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning" && c.Message.Contains("Basistabel") && c.Message.Contains("niet actief"));
    }

    [Fact]
    public async Task DerivedChain_BaseWindowDoesNotCoverDerivedWindow_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Base ends before the derived table's own window does — the derived table would go
        // "unpriced" (no rules resolvable) for the tail of its own validity.
        var baseAgreement = await CreateAgreementAsync(
            h, "Basistabel", isShared: true, effectiveUntil: Today.AddDays(30));
        await CreateRuleAsync(h, baseAgreement.Id, 50m);
        var derived = await CreateAgreementAsync(
            h, "Afgeleide tabel", isShared: true, baseAgreementId: baseAgreement.Id, effectiveUntil: null);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(derived.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Basistabel") && c.Message.Contains("dekt de geldigheidsperiode"));
    }

    [Fact]
    public async Task DerivedChain_ManufacturedCycle_ProducesError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        // Bypasses PricingAdminService validation entirely, the same way the engine-side runtime
        // guard test does — a cycle that should never be save-able must still be reported, not hang.
        h.Db.Context.PricingAgreements.Add(new PricingAgreement
        {
            Id = aId, TenantId = h.TenantId, Name = "Cyclus A", IsShared = true,
            EffectiveFrom = Today.AddMonths(-1), IsActive = true,
        });
        h.Db.Context.PricingAgreements.Add(new PricingAgreement
        {
            Id = bId, TenantId = h.TenantId, Name = "Cyclus B", IsShared = true,
            EffectiveFrom = Today.AddMonths(-1), IsActive = true, BaseAgreementId = aId,
        });
        await h.Db.Context.SaveChangesAsync();
        var trackedA = await h.Db.Context.PricingAgreements.FirstAsync(a => a.Id == aId);
        trackedA.BaseAgreementId = bId;
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(aId, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "error" && c.Message.Contains("Circulaire"));
    }

    // --- 5. Assignment window outside the agreement's own validity -> warning ------------------

    [Fact]
    public async Task AssignmentWindowOutsideAgreementValidity_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(
            h, "Beperkte geldigheid", isShared: true, effectiveFrom: Today.AddDays(-60), effectiveUntil: Today.AddDays(-30));
        await CreateRuleAsync(h, agreement.Id, 50m);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerId, null, null, Today, null, null)], CancellationToken.None);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Klant A") && c.Message.Contains("buiten de geldigheidsperiode"));
    }

    [Fact]
    public async Task AssignmentWindowInsideAgreementValidity_ProducesNoAssignmentWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Gedeelde tabel", isShared: true);
        await CreateRuleAsync(h, agreement.Id, 50m);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerId, null, null, null, null, null)], CancellationToken.None);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.DoesNotContain(checks!, c => c.Message.Contains("buiten de geldigheidsperiode"));
    }

    // --- 6. Shared agreement without any assignment -> warning ---------------------------------

    [Fact]
    public async Task SharedAgreementWithoutAssignment_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Ongekoppelde gedeelde tabel", isShared: true);
        await CreateRuleAsync(h, agreement.Id, 50m);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message == "Deze gedeelde tabel is aan geen enkele klant gekoppeld.");
    }

    [Fact]
    public async Task NonSharedAgreementWithoutAssignment_ProducesNoUnlinkedWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Bedrijfsbrede tabel", isShared: false);
        await CreateRuleAsync(h, agreement.Id, 50m);

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.DoesNotContain(checks!, c => c.Message.Contains("geen enkele klant gekoppeld"));
    }

    // --- 7. Rules referencing an inactive unit/zone -> warning ----------------------------------

    [Fact]
    public async Task RuleReferencingInactiveUnit_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Tabel met inactieve eenheid");
        await CreateRuleAsync(h, agreement.Id, 50m, "Regel op inactieve eenheid");
        var unit = await h.Db.Context.UnitTypes.FirstAsync(u => u.Id == h.PalletUnitId);
        unit.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Regel op inactieve eenheid") && c.Message.Contains("inactieve eenheid"));
    }

    [Fact]
    public async Task RuleReferencingInactiveZone_ProducesWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Tabel met inactieve zone");
        await CreateRuleAsync(h, agreement.Id, 50m, "Regel op inactieve zone", zoneId: h.ZoneId);
        var zone = await h.Db.Context.PricingZones.FirstAsync(z => z.Id == h.ZoneId);
        zone.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "warning"
            && c.Message.Contains("Regel op inactieve zone") && c.Message.Contains("inactieve zone"));
    }

    // --- 8. Agreement MinimumAmount > MaximumAmount (drifted data) -> error ---------------------

    [Fact]
    public async Task MinimumGreaterThanMaximum_DriftedData_ProducesError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateAgreementAsync(h, "Tabel met drift", minimumAmount: 50m, maximumAmount: 200m);
        await CreateRuleAsync(h, agreement.Id, 50m);

        // Normal saves can never produce Min > Max (ValidateAgreementAsync blocks it) — simulate
        // data drift directly, the same way other "should be impossible" tests in this codebase do.
        var tracked = await h.Db.Context.PricingAgreements.FirstAsync(a => a.Id == agreement.Id);
        tracked.MinimumAmount = 250m;
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(agreement.Id, CancellationToken.None);

        Assert.NotNull(checks);
        Assert.Contains(checks!, c => c.Severity == "error"
            && c.Message.Contains("250") && c.Message.Contains("200") && c.Message.Contains("hoger dan"));
    }

    // --- Tenant isolation ------------------------------------------------------------------------

    [Fact]
    public async Task TenantIsolation_ForeignAgreement_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        var foreignAgreementId = Guid.NewGuid();
        h.Db.Context.PricingAgreements.Add(new PricingAgreement
        {
            Id = foreignAgreementId, TenantId = otherTenantId, Name = "Vreemde tabel",
            EffectiveFrom = Today.AddMonths(-1), IsActive = true, IsShared = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var checks = await h.Admin.ValidateAgreementConfigurationAsync(foreignAgreementId, CancellationToken.None);

        Assert.Null(checks);
    }
}
