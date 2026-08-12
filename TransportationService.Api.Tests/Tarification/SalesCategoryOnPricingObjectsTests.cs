using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
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
/// Wave 2 §1: sales codes on pricing objects. Admin round-trip (agreement/rule/option carry a
/// SalesCategoryId), tenant validation, and the calculation-time stamping of the resolved code
/// onto the persisted order price/service lines (rule's own code wins over the engaged
/// agreement's; a service option carries its own).
/// </summary>
public class SalesCategoryOnPricingObjectsTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid TransportCatId, Guid ServiceCatId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var transportCatId = Guid.NewGuid();
        var serviceCatId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.SalesCategories.Add(new SalesCategory
        {
            Id = transportCatId, TenantId = tenantId, Code = "NAT-TRANS", Name = "Nationaal transport", IsActive = true,
        });
        db.Context.SalesCategories.Add(new SalesCategory
        {
            Id = serviceCatId, TenantId = tenantId, Code = "HANDLING", Name = "Handling", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, new PermissionSet());
        return new Harness(db, orders, admin, tenantId, customerId, palletUnitId, transportCatId, serviceCatId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, decimal quantity) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Pallets", quantity, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")],
        QuantityUnitCode: "EUROPALLET");

    // --- Admin round-trip ------------------------------------------------------------------

    [Fact]
    public async Task Admin_RoundTripsSalesCategory_OnAgreementRuleAndOption()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Contract X", new DateOnly(2026, 1, 1), null, true, null, null, null,
            SalesCategoryId: h.TransportCatId), CancellationToken.None);
        var rule = await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null,
            SalesCategoryId: h.ServiceCatId), CancellationToken.None);
        var option = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: h.PalletUnitId, SalesCategoryId: h.ServiceCatId), CancellationToken.None);

        Assert.Equal(h.TransportCatId, agreement.SalesCategoryId);
        Assert.Equal("Nationaal transport", agreement.SalesCategoryName);
        Assert.Equal(h.ServiceCatId, rule.SalesCategoryId);
        Assert.Equal("Handling", rule.SalesCategoryName);
        Assert.Equal(h.ServiceCatId, option.SalesCategoryId);
        Assert.Equal("Handling", option.SalesCategoryName);
    }

    [Fact]
    public async Task Admin_RejectsForeignSalesCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null,
            SalesCategoryId: Guid.NewGuid()), CancellationToken.None));
    }

    // --- Calculation-time stamping ---------------------------------------------------------

    [Fact]
    public async Task RuleOwnCode_WinsOverAgreementCode_AndIsStampedOnThePersistedLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Contract X", new DateOnly(2026, 1, 1), null, true, null, null, null,
            SalesCategoryId: h.TransportCatId), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null,
            AgreementId: agreement.Id, SalesCategoryId: h.ServiceCatId), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId, 8), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var stamped = await h.Db.Context.TransportOrderPricingLines
            .SingleAsync(l => l.TransportOrderId == created.Order!.Id && l.RuleId != null);
        Assert.Equal(h.ServiceCatId, stamped.SalesCategoryId);
    }

    [Fact]
    public async Task RuleWithoutOwnCode_InheritsTheEngagedAgreementCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Contract X", new DateOnly(2026, 1, 1), null, true, null, null, null,
            SalesCategoryId: h.TransportCatId), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null,
            AgreementId: agreement.Id), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId, 8), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var stamped = await h.Db.Context.TransportOrderPricingLines
            .SingleAsync(l => l.TransportOrderId == created.Order!.Id && l.RuleId != null);
        Assert.Equal(h.TransportCatId, stamped.SalesCategoryId);
    }

    [Fact]
    public async Task ServiceOptionCode_IsStampedOnThePersistedServiceLine_NullWhenUnconfigured()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: h.PalletUnitId, AutoApply: true, SalesCategoryId: h.ServiceCatId), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId, 8), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var serviceLine = await h.Db.Context.TransportOrderServiceLines
            .SingleAsync(l => l.TransportOrderId == created.Order!.Id);
        Assert.Equal(h.ServiceCatId, serviceLine.SalesCategoryId);

        // The base rule has no code configured → its persisted line stays unstamped (the
        // invoice-side role fallback keeps working exactly as before Wave 2).
        var ruleLine = await h.Db.Context.TransportOrderPricingLines
            .SingleAsync(l => l.TransportOrderId == created.Order!.Id && l.RuleId != null);
        Assert.Null(ruleLine.SalesCategoryId);
    }
}
