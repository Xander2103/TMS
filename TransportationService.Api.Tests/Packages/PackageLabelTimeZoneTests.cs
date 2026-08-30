using System.Text.Json;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Labels;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

/// <summary>
/// C-03: the pickup/delivery date and time printed on a physical package label are TENANT WALL
/// CLOCK, projected from the stored UTC instant. Before the one-transport-time-convention fix the
/// stored value happened to be the wall clock, so formatting it raw was accidentally right; after
/// the stop windows are re-encoded to true instants, a raw format prints 06:00 for an 08:00 pickup
/// on a label that is stuck to freight and cannot be recalled.
/// </summary>
public class PackageLabelTimeZoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, PackageLabelService Sut, PackageService Packages, Guid OrderId);

    private static async Task<Harness> SeedAsync(
        string timezone, DateTime? loadingPlannedFrom, DateTime? deliveryPlannedFrom)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Timezone = timezone,
            PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1, TradingName = "Acme Transport",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 22), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
            Stops =
            [
                new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading,
                    City = "Antwerpen", PlannedFrom = loadingPlannedFrom,
                },
                new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading,
                    City = "Rotterdam", PlannedFrom = deliveryPlannedFrom,
                },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, user);
        var sut = new PackageLabelService(
            db.Context, tenant, user, audit, new LabelRenderService(),
            new PackageEventWriter(db.Context, tenant, user, clock), clock);
        var packages = new PackageService(
            db.Context, tenant, audit, new PackageBarcodeService(db.Context, tenant, user, clock),
            new PackageEventWriter(db.Context, tenant, user, clock));
        return new Harness(db, sut, packages, orderId);
    }

    private static async Task<LabelSnapshot> PrintAsync(Harness h)
    {
        var package = (await h.Packages.CreateAsync(h.OrderId, new CreatePackageRequest("Doos"), CancellationToken.None)).Package!;
        var (pdf, error) = await h.Sut.PrintAsync([package.Id], LabelFormat.Thermal100x150, null, CancellationToken.None);
        Assert.Null(error);
        Assert.NotNull(pdf);
        return JsonSerializer.Deserialize<LabelSnapshot>(h.Db.Context.PackageLabels.Single().SnapshotJson, SnapshotJson)!;
    }

    [Fact]
    public async Task Label_PrintsTheTenantWallClock_NotTheStoredInstant()
    {
        // 08:00 and 14:00 local on Wednesday 22 July (CEST, +02:00) = 06:00Z and 12:00Z.
        var h = await SeedAsync("Europe/Amsterdam", Utc(2026, 7, 22, 6), Utc(2026, 7, 22, 12));
        using var _ = h.Db;

        var snapshot = await PrintAsync(h);

        Assert.Equal("22-07-2026", snapshot.PickupDate);
        Assert.Equal("08:00", snapshot.PickupTime);
        Assert.Equal("22-07-2026", snapshot.DeliveryDate);
        Assert.Equal("14:00", snapshot.DeliveryTime);
    }

    [Fact]
    public async Task Label_EarlyMorningStop_KeepsTheLocalCalendarDay()
    {
        // 00:30 local on 22 July is 21 July 22:30Z: a raw format prints "21-07-2026 22:30",
        // i.e. the wrong day on the label of a shipment that leaves on the 22nd.
        var h = await SeedAsync("Europe/Amsterdam", Utc(2026, 7, 21, 22, 30), Utc(2026, 7, 22, 12));
        using var _ = h.Db;

        var snapshot = await PrintAsync(h);

        Assert.Equal("22-07-2026", snapshot.PickupDate);
        Assert.Equal("00:30", snapshot.PickupTime);
    }

    [Fact]
    public async Task Label_Winter_UsesTheWinterOffset()
    {
        // 08:00 local on Monday 19 January (CET, +01:00) = 07:00Z.
        var h = await SeedAsync("Europe/Amsterdam", Utc(2026, 1, 19, 7), null);
        using var _ = h.Db;

        var snapshot = await PrintAsync(h);

        Assert.Equal("19-01-2026", snapshot.PickupDate);
        Assert.Equal("08:00", snapshot.PickupTime);
        Assert.Null(snapshot.DeliveryDate);
        Assert.Null(snapshot.DeliveryTime);
    }

    [Fact]
    public async Task Label_OnAUtcTenant_PrintsTheInstantUnchanged()
    {
        var h = await SeedAsync("UTC", Utc(2026, 7, 22, 6), Utc(2026, 7, 22, 12));
        using var _ = h.Db;

        var snapshot = await PrintAsync(h);

        Assert.Equal("06:00", snapshot.PickupTime);
        Assert.Equal("12:00", snapshot.DeliveryTime);
    }
}
