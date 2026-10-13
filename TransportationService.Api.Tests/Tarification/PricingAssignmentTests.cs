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
/// Reusable shared rate tables (spec S3/S6): a company-wide table (CustomerId null, IsShared)
/// that only prices an order for a customer with an active, date-bound assignment, plus a
/// per-customer percent/fixed adjustment on top of the table and a maximum-amount cap.
/// </summary>
public class PricingAssignmentTests
{
    private static readonly DateOnly Today = new(2026, 7, 25);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerAId, Guid CustomerBId, Guid CustomerXId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var customerXId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerAId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerBId, TenantId = tenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerXId, TenantId = tenantId, Name = "Klant X", CustomerNumber = "KL-X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerAId, customerBId, customerXId, palletUnitId);
    }

    private static PriceCalculationRequest Request(
        Harness h, decimal quantity, Guid customerId, string? postalCode = null, DateOnly? date = null) =>
        new(customerId, date ?? Today, [new PriceCalculationLineInput(h.PalletUnitId, quantity)],
            "BE", postalCode, null, null, null, []);

    private static Task<PricingAgreementDto> CreateSharedAgreementAsync(
        Harness h, string name = "Distributie België 2026", decimal? maximum = null) =>
        h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, name, Today.AddMonths(-1), null, true, null, null, null,
            IsShared: true, MaximumAmount: maximum), CancellationToken.None);

    /// <summary>The spec S3 example table: 1 pallet €50, 2 = €80, 3+ = €105.</summary>
    private static Task<PriceRuleDto> CreateThreeTierBracketRuleAsync(
        Harness h, Guid agreementId, string name = "Pallets gedeeld", Guid? zoneId = null) =>
        h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, zoneId,
            name, Today.AddMonths(-1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50m, null),
                new SavePriceRuleBracketRequest(2, 2, 80m, null),
                new SavePriceRuleBracketRequest(3, null, 105m, null),
            ], AgreementId: agreementId), CancellationToken.None);

    [Fact]
    public async Task Shared_AppliesToAllAssignedCustomers_WithSameAgreementId_NoDuplication()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [
                new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null),
                new SavePricingAssignmentRequest(h.CustomerBId, null, null, null, null, null),
            ], CancellationToken.None);

        var resultA = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);
        var resultB = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerBId), CancellationToken.None);

        Assert.Equal(105m, resultA.Total);
        Assert.Equal(105m, resultB.Total);
        var lineA = Assert.Single(resultA.Lines, l => l.AgreementId is not null);
        var lineB = Assert.Single(resultB.Lines, l => l.AgreementId is not null);
        Assert.Equal(agreement.Id, lineA.AgreementId);
        Assert.Equal(agreement.Id, lineB.AgreementId);
    }

    [Fact]
    public async Task Shared_AssignmentWindowExpiredBeforeTariffDate_DoesNotApply()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, Today.AddMonths(-6), Today.AddMonths(-1), null)],
            CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task Shared_PercentAdjustment_AppliesToSubtotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, -5m, null, null, null, null)], CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);

        Assert.Contains(result.Lines, l => l.Label == "Klantafspraak -5%" && l.Amount == -5.25m);
        Assert.Equal(99.75m, result.Total);
    }

    [Fact]
    public async Task Shared_FixedAdjustment_AddsFlatAmountOnTopOfTable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, 10m, null, null, null)], CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);

        Assert.Contains(result.Lines, l => l.Label == "Klantafspraak vast bedrag" && l.Amount == 10m);
        Assert.Equal(115m, result.Total);
    }

    [Fact]
    public async Task PrivateCustomerRule_OverridesSharedAssignment_S6()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [
                new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null),
                new SavePricingAssignmentRequest(h.CustomerXId, null, null, null, null, null),
            ], CancellationToken.None);
        // Customer X negotiated their own, better price for the same bracket.
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerXId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets Klant X eigen tarief", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 99m, null)]), CancellationToken.None);

        var resultX = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerXId), CancellationToken.None);
        var resultA = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);

        Assert.Equal(99m, resultX.Total);
        Assert.Equal(105m, resultA.Total);
    }

    [Fact]
    public async Task Shared_WithoutAssignment_DoesNotApplyToAnyone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);

        var result = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task Precedence_PrivateBeatsAssigned_AssignedBeatsCompanyDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zone = await h.Admin.CreateZoneAsync(new SavePricingZoneRequest("Z3", "Zone 3", true, 0,
            [new SavePricingZoneAreaRequest("BE", "3000", "3999")]), CancellationToken.None);

        // Private rule for A: no zone (tier 2, score 8).
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Privé A", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 60m, null)]), CancellationToken.None);

        // Shared table assigned only to A, WITH a zone (tier 1, score 6) — private still wins.
        var sharedForA = await CreateSharedAgreementAsync(h, "Gedeelde tabel A");
        await CreateThreeTierBracketRuleAsync(h, sharedForA.Id, "Gedeeld A Z3", zone.Id);
        await h.Admin.SaveAssignmentsAsync(sharedForA.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        // Shared table assigned only to B, WITHOUT a zone (tier 1, score 4).
        var sharedForB = await CreateSharedAgreementAsync(h, "Gedeelde tabel B");
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Gedeeld B", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 40m, null)], AgreementId: sharedForB.Id), CancellationToken.None);
        await h.Admin.SaveAssignmentsAsync(sharedForB.Id,
            [new SavePricingAssignmentRequest(h.CustomerBId, null, null, null, null, null)], CancellationToken.None);

        // Company default, WITH a zone (tier 0, score 2) — still loses to the assigned table for B.
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, zone.Id,
            "Standaard Z3", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 90m, null)]), CancellationToken.None);

        var resultA = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId, postalCode: "3500"), CancellationToken.None);
        var resultB = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerBId, postalCode: "3500"), CancellationToken.None);

        Assert.Equal(60m, resultA.Total);
        Assert.Equal(40m, resultB.Total);
    }

    [Fact]
    public async Task ExactTie_WithinSameTier_StillBlocksNamingBothRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement1 = await CreateSharedAgreementAsync(h, "Tabel 1");
        var agreement2 = await CreateSharedAgreementAsync(h, "Tabel 2");
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Regel Tabel 1", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 50m, null)], AgreementId: agreement1.Id), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Regel Tabel 2", Today.AddMonths(-1), null, true, null, null,
            [new SavePriceRuleBracketRequest(3, null, 60m, null)], AgreementId: agreement2.Id), CancellationToken.None);
        await h.Admin.SaveAssignmentsAsync(agreement1.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);
        await h.Admin.SaveAssignmentsAsync(agreement2.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var result = await h.Engine.CalculateAsync(Request(h, 3, h.CustomerAId), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Regel Tabel 1", result.ConfigurationError);
        Assert.Contains("Regel Tabel 2", result.ConfigurationError);
    }

    [Fact]
    public async Task MaximumAmount_CapsSubtotal_BeforeSurchargeIsComputed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerAId, "Distributie 2026", Today.AddMonths(-1), null, true, null, null,
            [new SavePricingAgreementSurchargeRequest("Duurtoeslag", SurchargeKind.Percent, 10m)],
            MaximumAmount: 100m), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerAId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", Today.AddMonths(-1), null, true, 65m, null, null,
            AgreementId: agreement.Id), CancellationToken.None);

        // 2 × 65 = 130 → capped at 100 → +10% surcharge on the capped 100 = 110.
        var result = await h.Engine.CalculateAsync(Request(h, 2, h.CustomerAId), CancellationToken.None);

        Assert.Contains(result.Lines, l => l.Label == "Maximumtarief Distributie 2026" && l.Amount == -30m);
        Assert.Contains(result.Lines, l => l.Label == "Duurtoeslag" && l.Amount == 10m);
        Assert.Equal(110m, result.Total);
    }

    [Fact]
    public async Task Validation_SharedTableAndAssignmentRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // IsShared with a CustomerId is rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(h.CustomerAId, "Fout", Today.AddMonths(-1), null, true, null, null, null,
                IsShared: true), CancellationToken.None));

        // Maximum below minimum is rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(h.CustomerAId, "Max onder min", Today.AddMonths(-1), null, true, 50m, null, null,
                MaximumAmount: 20m), CancellationToken.None));

        // Assignments on a non-shared agreement are rejected.
        var plain = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerAId, "Gewoon", Today.AddMonths(-1), null, true, null, null, null), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.SaveAssignmentsAsync(plain.Id,
            [new SavePricingAssignmentRequest(h.CustomerBId, null, null, null, null, null)], CancellationToken.None));

        var shared = await CreateSharedAgreementAsync(h);

        // Overlapping assignment windows for the same customer are rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.SaveAssignmentsAsync(shared.Id,
            [
                new SavePricingAssignmentRequest(h.CustomerAId, null, null, Today.AddMonths(-1), Today.AddMonths(2), null),
                new SavePricingAssignmentRequest(h.CustomerAId, null, null, Today.AddMonths(1), null, null),
            ], CancellationToken.None));

        // Percent adjustment out of ±100 range is rejected.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.SaveAssignmentsAsync(shared.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, 150m, null, null, null, null)], CancellationToken.None));
    }

    [Fact]
    public async Task TenantIsolation_AssignmentsAndSharedAgreementsAreNotVisibleAcrossTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await CreateSharedAgreementAsync(h);
        await CreateThreeTierBracketRuleAsync(h, agreement.Id);
        await h.Admin.SaveAssignmentsAsync(agreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = foreignTenantId, Name = "Ander", CustomerNumber = "X-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignAdmin = new PricingAdminService(h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)));
        var foreignEngine = new PricingEngine(h.Db.Context, foreignTenant);

        Assert.Empty(await foreignAdmin.ListAgreementsAsync(null, CancellationToken.None));
        Assert.Null(await foreignAdmin.ListAssignmentsAsync(agreement.Id, CancellationToken.None));

        var result = await foreignEngine.CalculateAsync(
            new PriceCalculationRequest(otherCustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, 3)], "BE", null, null, null, null, []),
            CancellationToken.None);
        Assert.True(result.RequiresManualPrice);
    }
}
