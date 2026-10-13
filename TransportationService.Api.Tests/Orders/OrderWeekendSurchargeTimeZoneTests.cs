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
/// C-03, money path: <c>StopTimeInput.PlannedDate</c> drives the WEEKEND and HOLIDAY surcharges
/// (<c>PricingEngine</c> <c>ServiceConditionKind.Weekend</c>/<c>Holiday</c>). A stop window is a UTC
/// instant, so the calendar day it belongs to is the TENANT-LOCAL one. Truncating the raw instant
/// prices a Monday 00:30 stop as weekend work and silently drops the surcharge from a Saturday
/// 00:30 stop — an invoice that is wrong in both directions.
/// </summary>
public class OrderWeekendSurchargeTimeZoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 05, 12, 0, 0, TimeSpan.Zero);

    /// <summary>2026-08-08 is a Saturday; 2026-08-07 a Friday; 2026-08-10 a Monday.</summary>
    private static readonly DateOnly OrderDate = new(2026, 8, 7);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid CustomerId);

    private static async Task<Harness> SeedAsync(string timezone)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Timezone = timezone,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);

        // Base price: 10 per pallet, 3 pallets = 30.
        await admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, palletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", OrderDate.AddMonths(-1), null, true, 10m, null, null), CancellationToken.None);

        // Weekend surcharge on the unloading stop: +40.
        await admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "WKND", "Weekendlevering", SurchargeKind.Fixed, 40m, true, 0,
            AutoApply: true,
            TimeConditions:
            [
                new ServiceTimeConditionDto(
                    ServiceConditionKind.Weekend, ServiceConditionStopScope.Unloading, null, 0, false),
            ]), CancellationToken.None);

        var sut = new TransportOrderService(
            db.Context, tenant, audit, new TestClock(Now), new PricingEngine(db.Context, tenant));
        return new Harness(db, sut, customerId);
    }

    private static async Task<decimal?> PriceAsync(Harness h, DateTime unloadingPlannedFrom)
    {
        var created = await h.Sut.CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, "REF-1", OrderDate, "Pallets", 3, null, null, null, null, false, false, null, null,
            [
                new TransportOrderStopInput(StopType.Loading, null, null, null, null, "Antwerpen", "BE", null, null, null, null),
                new TransportOrderStopInput(StopType.Unloading, null, null, null, "3500", "Hasselt", "BE",
                    unloadingPlannedFrom, null, null, null),
            ],
            QuantityUnitCode: "EUROPALLET"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        return created.Order!.CalculatedPrice;
    }

    [Fact]
    public async Task SaturdayJustAfterLocalMidnight_IsPricedAsWeekend()
    {
        var h = await SeedAsync("Europe/Amsterdam");
        using var _ = h.Db;

        // Saturday 08-08 00:30 local (CEST, +02:00) = Friday 07-08 22:30Z. The raw instant reads
        // Friday, so the weekend surcharge would be silently dropped from the invoice.
        Assert.Equal(70m, await PriceAsync(h, Utc(2026, 8, 7, 22, 30)));
    }

    [Fact]
    public async Task FridayLateEvening_IsNotWeekend()
    {
        var h = await SeedAsync("Europe/Amsterdam");
        using var _ = h.Db;

        // Friday 07-08 23:30 local = 21:30Z — same UTC day as the case above, different local day.
        Assert.Equal(30m, await PriceAsync(h, Utc(2026, 8, 7, 21, 30)));
    }

    [Fact]
    public async Task MondayJustAfterLocalMidnight_IsNotWeekend()
    {
        var h = await SeedAsync("Europe/Amsterdam");
        using var _ = h.Db;

        // Monday 10-08 00:30 local = Sunday 09-08 22:30Z. The mirror error: the raw instant reads
        // Sunday and would ADD a weekend surcharge to an ordinary Monday delivery.
        Assert.Equal(30m, await PriceAsync(h, Utc(2026, 8, 9, 22, 30)));
    }

    [Fact]
    public async Task OnAUtcTenant_TheRawInstantIsTheLocalDay()
    {
        var h = await SeedAsync("UTC");
        using var _ = h.Db;

        // Control: on a UTC tenant Friday 22:30Z IS Friday, so no surcharge — and the Saturday
        // instant still gets one. Only the tenant zone separates this from the first case.
        Assert.Equal(30m, await PriceAsync(h, Utc(2026, 8, 7, 22, 30)));
        Assert.Equal(70m, await PriceAsync(h, Utc(2026, 8, 8, 2, 0)));
    }
}
