using Microsoft.EntityFrameworkCore;
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

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Corrections wave 2026-07-27 §2.3: per-day and per-pallet-day services get usable order
/// inputs — day count for PerDay, pallets × days (with manual correction) for PerPalletDay —
/// persisted on the service line and reproduced by recalculation.
/// </summary>
public class OrderDayQuantityTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, new PermissionSet());
        return new Harness(db, sut, admin, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId) => new(
        customerId, "REF-1", new DateOnly(2026, 7, 27), "Pallets", 3, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        null, QuantityUnitCode: "EUROPALLET");

    private static async Task SeedBaseRuleAsync(Harness h) =>
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 10m, null, null), CancellationToken.None);

    [Fact]
    public async Task PerDayService_UsesDayCount_AsBillableQuantity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        var opslag = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "OPSLAG", "Opslag", SurchargeKind.PerDay, 0.25m, true, 0), CancellationToken.None);

        // Spec example: 12 dagen × €0,25 = €3,00.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId) with
        {
            Services = [new OrderServiceInput(opslag.Id, DayCount: 12m)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var line = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal(3.00m, line.Amount);
        Assert.Equal(12m, line.Quantity);
        Assert.Equal(12m, line.DayCount);

        var stored = await h.Db.Context.TransportOrderServiceLines.SingleAsync();
        Assert.Equal(12m, stored.Quantity);
        Assert.Equal(12m, stored.DayCount);
        Assert.Equal(33m, created.Order.AgreedPrice); // 3 × €10 + €3
    }

    [Fact]
    public async Task PerPalletDayService_DerivesPalletsTimesDays_AndRecalculationReproducesIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        var opslag = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PALDAG", "Palletopslag", SurchargeKind.PerPalletDay, 0.20m, true, 0), CancellationToken.None);

        // Spec example: 4 pallets × 12 dagen = 48 pallet-dagen × €0,20 = €9,60.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId) with
        {
            Services = [new OrderServiceInput(opslag.Id, PalletCount: 4m, DayCount: 12m)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var line = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal(9.60m, line.Amount);
        Assert.Equal(48m, line.Quantity);
        Assert.Equal(4m, line.PalletCount);
        Assert.Equal(12m, line.DayCount);

        // Recalculation rebuilds from the persisted inputs and reproduces the same numbers.
        var orderId = (await h.Db.Context.TransportOrders.SingleAsync()).Id;
        var recalculated = await h.Sut.RecalculateOrderPricingAsync(orderId, CancellationToken.None);
        Assert.NotNull(recalculated);
        var storedAfter = await h.Db.Context.TransportOrderServiceLines.SingleAsync();
        Assert.Equal(48m, storedAfter.Quantity);
        Assert.Equal(4m, storedAfter.PalletCount);
        Assert.Equal(12m, storedAfter.DayCount);
        Assert.Equal(9.60m, storedAfter.Amount);
    }

    [Fact]
    public async Task ExplicitQuantity_IsAManualCorrection_ThatBeatsPalletsTimesDays()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        var opslag = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PALDAG", "Palletopslag", SurchargeKind.PerPalletDay, 0.20m, true, 0), CancellationToken.None);

        // Reality differed: 50 pallet-days billed despite 4 × 12 = 48.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId) with
        {
            Services = [new OrderServiceInput(opslag.Id, Quantity: 50m, PalletCount: 4m, DayCount: 12m)],
        }, CancellationToken.None);

        var line = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal(10.00m, line.Amount); // 50 × €0,20
        Assert.Equal(50m, line.Quantity);
        Assert.Equal(4m, line.PalletCount);
        Assert.Equal(12m, line.DayCount);
    }

    [Fact]
    public async Task MissingDayCount_YieldsInformationalPrompt_NeverASilentCharge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        var opslag = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "OPSLAG", "Opslag", SurchargeKind.PerDay, 0.25m, true, 0), CancellationToken.None);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId) with
        {
            Services = [new OrderServiceInput(opslag.Id)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Empty(created.Order!.ServiceLines!);
        Assert.Contains(created.Order.PricingLines!, l => l.Label.Contains("geef het aantal dagen op"));
        Assert.Equal(30m, created.Order.AgreedPrice); // base only, no silent €0/charge
    }
}
