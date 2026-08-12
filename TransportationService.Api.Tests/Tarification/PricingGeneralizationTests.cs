using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
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
/// Wave 3 §1: the order finally supplies DistanceKm/LoadingMeters — the engine's PerKm and
/// PerLoadingMeter bases (which always existed) fire from real order data. Golden protection:
/// an order WITHOUT the new inputs prices exactly as before (the rules report "requires
/// manual" instead of inventing a distance).
/// </summary>
public class PricingGeneralizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 14, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now),
            new PricingEngine(db.Context, tenant), user, new PermissionSet());
        return new Harness(db, orders, new PricingAdminService(db.Context, tenant, audit), tenantId, customerId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(
        Guid customerId, decimal? distanceKm = null, decimal? loadingMeters = null) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Machinetransport", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")],
        DistanceKm: distanceKm, LoadingMeters: loadingMeters);

    [Fact]
    public async Task PerKmRule_Fires_FromTheOrderDistance()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Kilometertarief", new DateOnly(2026, 1, 1), null, true, 1.50m, null, null), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId, distanceKm: 120), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(180.00m, created.Order!.AgreedPrice);
        Assert.Equal(120m, created.Order.DistanceKm);
    }

    [Fact]
    public async Task PerLdmRule_Fires_FromTheOrderLoadingMeters()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerLoadingMeter, null,
            "Laadmetertarief", new DateOnly(2026, 1, 1), null, true, 40m, null, null), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId, loadingMeters: 3.5m), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(140.00m, created.Order!.AgreedPrice);
        Assert.Equal(3.5m, created.Order.LoadingMeters);
    }

    /// <summary>Golden: no distance supplied = exactly the pre-wave behavior (no invented km).</summary>
    [Fact]
    public async Task PerKmRule_WithoutOrderDistance_StillRequiresAManualPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Kilometertarief", new DateOnly(2026, 1, 1), null, true, 1.50m, null, null), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Null(created.Order!.AgreedPrice);
    }

    [Fact]
    public async Task DistanceAndLoadingMeters_RoundTripThroughTheDetailDto_AndAreLockedPricingInputs()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Orders.CreateAsync(Request(h.CustomerId, 120, 3.5m), CancellationToken.None);
        var order = created.Order!;
        Assert.Equal(120m, order.DistanceKm);
        Assert.Equal(3.5m, order.LoadingMeters);

        // Changing the distance is a pricing-input change: with a LOCKED snapshot it must refuse.
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots
            .SingleAsync(s => s.TransportOrderId == order.Id);
        snapshot.Status = OrderPricingStatus.Locked;
        await h.Db.Context.SaveChangesAsync();

        var update = new UpdateTransportOrderRequest(
            order.CustomerId, order.CustomerReference, order.OrderDate, order.GoodsDescription, order.Quantity,
            order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount, order.AdrRequired, order.CraneRequired,
            order.AgreedPrice, order.Notes,
            order.Stops.Select(s => new TransportOrderStopInput(
                    s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
                .ToList(),
            DistanceKm: 200, LoadingMeters: order.LoadingMeters);

        var ex = await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(
            () => h.Orders.UpdateAsync(order.Id, update, CancellationToken.None));
        Assert.Contains("vergrendeld", ex.Message);
    }
}
