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

    // --- §2: origin zone / O-D dimension -----------------------------------------------------

    private static async Task<Guid> ZoneAsync(Harness h, string code, string postalFrom, string postalTo)
    {
        var zone = new PricingZone { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = code, Name = code, IsActive = true };
        zone.Areas.Add(new PricingZoneArea
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, ZoneId = zone.Id,
            CountryCode = "BE", PostalCodeFrom = postalFrom, PostalCodeTo = postalTo,
        });
        h.Db.Context.PricingZones.Add(zone);
        await h.Db.Context.SaveChangesAsync();
        return zone.Id;
    }

    private static CreateTransportOrderRequest OdRequest(Guid customerId, string loadingPostal) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Machinetransport", null, null, null, null, null, false, false,
        null, null,
        [
            new TransportOrderStopInput(StopType.Loading, null, null, null, loadingPostal, "Laadplaats", "BE", null, null, null, null),
            new TransportOrderStopInput(StopType.Unloading, null, null, null, "3500", "Hasselt", "BE", null, null, null, null),
        ],
        DistanceKm: 100);

    [Fact]
    public async Task OriginZoneRule_AppliesOnlyWhenTheFirstLoadingStopLandsInTheZone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var antwerpZoneId = await ZoneAsync(h, "ANT", "2000", "2999");
        // Generic km rate 1.50; Antwerp-origin km rate 2.00 (more specific → wins for Antwerp).
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Km algemeen", new DateOnly(2026, 1, 1), null, true, 1.50m, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Km vanuit Antwerpen", new DateOnly(2026, 1, 1), null, true, 2.00m, null, null,
            OriginZoneId: antwerpZoneId), CancellationToken.None);

        var fromAntwerp = await h.Orders.CreateAsync(OdRequest(h.CustomerId, "2000"), CancellationToken.None);
        Assert.Equal(200.00m, fromAntwerp.Order!.AgreedPrice);

        var fromGhent = await h.Orders.CreateAsync(OdRequest(h.CustomerId, "9000"), CancellationToken.None);
        Assert.Equal(150.00m, fromGhent.Order!.AgreedPrice);
    }

    [Fact]
    public async Task DestinationZone_StaysTheStrongerTiebreaker_OverOriginZone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var antwerpZoneId = await ZoneAsync(h, "ANT", "2000", "2999");
        var limburgZoneId = await ZoneAsync(h, "LIM", "3500", "3999");
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, limburgZoneId,
            "Km naar Limburg", new DateOnly(2026, 1, 1), null, true, 3.00m, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Km vanuit Antwerpen", new DateOnly(2026, 1, 1), null, true, 2.00m, null, null,
            OriginZoneId: antwerpZoneId), CancellationToken.None);

        // Both match (origin Antwerpen, destination Limburg): the destination-zone rule wins.
        var created = await h.Orders.CreateAsync(OdRequest(h.CustomerId, "2000"), CancellationToken.None);
        Assert.Equal(300.00m, created.Order!.AgreedPrice);
    }

    // --- §3: Maut as a sales-side PerKm service ----------------------------------------------

    [Fact]
    public async Task PerKmService_AutoApplies_TimesTheOrderDistance()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, null, PriceRuleBasis.PerKm, null,
            "Kilometertarief", new DateOnly(2026, 1, 1), null, true, 1.50m, null, null), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "MAUT", "Maut-toeslag", SurchargeKind.PerKm, 0.19m, true, 0, AutoApply: true), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId, distanceKm: 100), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var maut = await h.Db.Context.TransportOrderServiceLines
            .SingleAsync(l => l.TransportOrderId == created.Order!.Id);
        Assert.Equal(19.00m, maut.Amount);
        Assert.Equal(100m, maut.Quantity);
        // 100 × 1.50 base + 19.00 Maut.
        Assert.Equal(169.00m, created.Order.AgreedPrice);
    }

    [Fact]
    public async Task PerKmService_WithoutDistance_StaysInformational_NeverASilentZeroCharge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "MAUT", "Maut-toeslag", SurchargeKind.PerKm, 0.19m, true, 0, AutoApply: true), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Empty(await h.Db.Context.TransportOrderServiceLines
            .Where(l => l.TransportOrderId == created.Order!.Id).ToListAsync());
    }

    // --- §4: holiday calendar ----------------------------------------------------------------

    [Fact]
    public async Task HolidayCondition_FiresOnAConfiguredHoliday_NotOnOrdinaryDays()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateHolidayAsync(new SaveTenantHolidayRequest(
            new DateOnly(2026, 11, 11), "Wapenstilstand"), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "FEEST", "Feestdagtoeslag", SurchargeKind.Fixed, 75m, true, 0, AutoApply: true,
            TimeConditions: [new ServiceTimeConditionDto(ServiceConditionKind.Holiday)]), CancellationToken.None);

        CreateTransportOrderRequest DatedRequest(DateTime plannedUnloading) => new(
            h.CustomerId, "REF-1", new DateOnly(2026, 11, 10), "Pallets", null, null, null, null, null, false, false,
            null, null,
            [
                Stop(StopType.Loading, "Antwerpen"),
                new TransportOrderStopInput(StopType.Unloading, null, null, null, "3500", "Hasselt", "BE",
                    plannedUnloading, plannedUnloading.AddHours(2), null, null),
            ]);

        var onHoliday = await h.Orders.CreateAsync(
            DatedRequest(new DateTime(2026, 11, 11, 9, 0, 0)), CancellationToken.None);
        var holidayLine = await h.Db.Context.TransportOrderServiceLines
            .SingleAsync(l => l.TransportOrderId == onHoliday.Order!.Id);
        Assert.Equal(75m, holidayLine.Amount);

        var ordinaryDay = await h.Orders.CreateAsync(
            DatedRequest(new DateTime(2026, 11, 12, 9, 0, 0)), CancellationToken.None);
        Assert.Empty(await h.Db.Context.TransportOrderServiceLines
            .Where(l => l.TransportOrderId == ordinaryDay.Order!.Id).ToListAsync());
    }

    [Fact]
    public async Task HolidayAdmin_RejectsDuplicateDates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateHolidayAsync(new SaveTenantHolidayRequest(
            new DateOnly(2026, 12, 25), "Kerstmis"), CancellationToken.None);

        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(
            () => h.Admin.CreateHolidayAsync(new SaveTenantHolidayRequest(
                new DateOnly(2026, 12, 25), "Dubbel"), CancellationToken.None));
    }
}
