using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>
/// P6: the new pricing dimensions — activity-bound rules (crane vs plateau in one dossier),
/// equipment conditions (Moffett/plateau), return-movement pricing — with legacy behavior
/// byte-stable when the dimensions are absent.
/// </summary>
public class PricingDimensionTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 14, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, PricingAdminService Admin, PricingEngine Engine,
        Guid TenantId, Guid CustomerId, Guid CraneTypeId, Guid PlateauTypeId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var craneTypeId = Guid.NewGuid();
        var plateauTypeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.ActivityTypes.AddRange(
            new ActivityType
            {
                Id = craneTypeId, TenantId = tenantId, Code = "KRAANWERK", Name = "Kraanwerk",
                HasStops = true, IsActive = true, IsSystemDefaultTransport = true,
            },
            new ActivityType
            {
                Id = plateauTypeId, TenantId = tenantId, Code = "PLATEAU", Name = "Plateau",
                HasStops = true, IsActive = true,
            });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var engine = new PricingEngine(db.Context, tenant);
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now),
            engine, user, new PermissionSet());
        return new Harness(db, orders, new PricingAdminService(db.Context, tenant, audit), engine,
            tenantId, customerId, craneTypeId, plateauTypeId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(
        Guid customerId, bool moffett = false, bool isReturn = false) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Machinetransport", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")],
        DistanceKm: 100,
        MoffettRequired: moffett, IsReturnMovement: isReturn);

    private async Task<SavePriceRuleRequest> KmRule(
        Harness h, string name, decimal rate, Guid? activityTypeId = null)
    {
        var request = new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            name, new DateOnly(2026, 1, 1), null, true, rate, null, null,
            ActivityTypeId: activityTypeId);
        await h.Admin.CreateRuleAsync(request, CancellationToken.None);
        return request;
    }

    // Example A + E: an activity-bound rule only prices matching activities; crane and plateau
    // price separately even inside one dossier.
    [Fact]
    public async Task ActivityBoundRules_PriceCraneAndPlateauSeparately()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await KmRule(h, "Kraantarief", 3.00m, h.CraneTypeId);
        await KmRule(h, "Plateautarief", 2.00m, h.PlateauTypeId);
        await KmRule(h, "Algemeen tarief", 1.00m);

        // Order created without explicit dossier: auto-wrap creates a KRAANWERK (default) activity.
        var craneOrder = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, craneOrder.Outcome);
        Assert.Equal(300.00m, craneOrder.Order!.AgreedPrice); // 100 km × €3 (kraanregel wint van algemeen)

        // Rewire the linked activity to PLATEAU and recalc: the plateau rule prices it.
        var activity = h.Db.Context.DossierActivities.Single(a => a.LinkedTransportOrderId == craneOrder.Order.Id);
        activity.ActivityTypeId = h.PlateauTypeId;
        await h.Db.Context.SaveChangesAsync();
        var updated = await h.Orders.UpdateAsync(craneOrder.Order.Id, new UpdateTransportOrderRequest(
            h.CustomerId, "REF-1", new DateOnly(2026, 8, 12), "Machinetransport", null, null, null, null, null, false, false,
            null, null,
            [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")],
            DistanceKm: 100), CancellationToken.None);
        Assert.Equal(200.00m, updated.Order!.AgreedPrice); // 100 km × €2
    }

    // Legacy byte-stability: without any new dimension the generic rule prices exactly as before.
    [Fact]
    public async Task LegacyRules_WithoutDimensions_AreUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await KmRule(h, "Algemeen tarief", 1.50m);

        var order = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);

        Assert.Equal(150.00m, order.Order!.AgreedPrice);
    }

    // Example B: a Moffett surcharge applies only when the order needs a Moffett.
    [Fact]
    public async Task MoffettCondition_GatesTheSurcharge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await KmRule(h, "Algemeen tarief", 1.00m);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "MOFFETT", "Moffett-toeslag", SurchargeKind.Fixed, 45m, true, 1,
            AutoApply: true,
            TimeConditions: [new ServiceTimeConditionDto(ServiceConditionKind.Moffett)]), CancellationToken.None);

        var with = await h.Orders.CreateAsync(Request(h.CustomerId, moffett: true), CancellationToken.None);
        Assert.Equal(145.00m, with.Order!.AgreedPrice); // 100 + 45

        var without = await h.Orders.CreateAsync(Request(h.CustomerId, moffett: false), CancellationToken.None);
        Assert.Equal(100.00m, without.Order!.AgreedPrice);
    }

    // Examples C + D: return pricing fires on a return movement and stays silent otherwise.
    [Fact]
    public async Task ReturnMovementCondition_AppliesOnlyOnRetour()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await KmRule(h, "Algemeen tarief", 1.00m);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "RETOUR", "Retourtoeslag", SurchargeKind.Fixed, 30m, true, 1,
            AutoApply: true,
            TimeConditions: [new ServiceTimeConditionDto(ServiceConditionKind.ReturnMovement)]), CancellationToken.None);

        var retour = await h.Orders.CreateAsync(Request(h.CustomerId, isReturn: true), CancellationToken.None);
        Assert.Equal(130.00m, retour.Order!.AgreedPrice);

        var normal = await h.Orders.CreateAsync(Request(h.CustomerId, isReturn: false), CancellationToken.None);
        Assert.Equal(100.00m, normal.Order!.AgreedPrice);
    }

    // The activity condition on a SERVICE follows the linked activity type.
    [Fact]
    public async Task ActivityTypeCondition_GatesAService()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await KmRule(h, "Algemeen tarief", 1.00m);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "KRAANTOESLAG", "Kraantoeslag", SurchargeKind.Fixed, 60m, true, 1,
            AutoApply: true,
            TimeConditions:
            [
                new ServiceTimeConditionDto(ServiceConditionKind.ActivityType, ActivityTypeId: h.CraneTypeId),
            ]), CancellationToken.None);

        // Auto-wrap gives the order the KRAANWERK default activity → the surcharge applies.
        var crane = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        Assert.Equal(160.00m, crane.Order!.AgreedPrice);
    }
}
