using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Phase 7: advisory opening-hours warnings on the order detail. Planned stop times are
/// evaluated against the LIVE location intervals (the warning answers "will the site be
/// open"; the snapshot answers "what did we agree"). Never blocking, Dutch messages.
/// </summary>
public class StopOpeningHoursWarningTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    // 2026-07-20 is a Monday; the seeded location opens Ma 08:00–12:00 and 13:00–17:00.
    private static readonly DateTime Monday = new(2026, 7, 20);
    private static readonly DateTime Sunday = new(2026, 7, 19);

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync(bool withIntervals = true)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Magazijn Antwerpen",
            City = "Antwerpen", CountryCode = "BE", Type = LocationType.Warehouse, IsActive = true,
            OpeningIntervals = withIntervals
                ?
                [
                    new LocationOpeningInterval { Id = Guid.NewGuid(), TenantId = tenantId, LocationId = locationId, DayOfWeek = 1, FromTime = new(8, 0), ToTime = new(12, 0) },
                    new LocationOpeningInterval { Id = Guid.NewGuid(), TenantId = tenantId, LocationId = locationId, DayOfWeek = 1, FromTime = new(13, 0), ToTime = new(17, 0) },
                ]
                : [],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TransportOrderService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new TestClock(Now));
        return new Harness(db, sut, tenantId, customerId, locationId);
    }

    private static CreateTransportOrderRequest Request(
        Guid customerId, Guid locationId, DateTime? plannedFrom, DateTime? plannedTo = null) => new(
        customerId, "PO-777", new DateOnly(2026, 7, 20), "Paletten",
        20, "paletten", null, null, null, false, false, null, null,
        [
            new TransportOrderStopInput(StopType.Loading, locationId, null, null, null, null, null, plannedFrom, plannedTo, null, null),
            new TransportOrderStopInput(StopType.Unloading, null, null, null, null, "Gent", "BE", null, null, null, null),
        ]);

    private static async Task<TransportOrderStopDto> LocationStopAsync(Harness h, DateTime? from, DateTime? to = null)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, h.LocationId, from, to), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        return created.Order!.Stops.Single(s => s.LocationId == h.LocationId);
    }

    [Fact]
    public async Task InsideOpeningHours_NoWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stop = await LocationStopAsync(h, Monday.AddHours(9), Monday.AddHours(11));
        Assert.Null(stop.Warnings);
    }

    [Fact]
    public async Task BeforeOpening_WarnsWithHoursAndName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stop = await LocationStopAsync(h, Monday.AddHours(7).AddMinutes(30), Monday.AddHours(9));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Equal(
            "De geplande laadtijd van 07:30 valt buiten de openingsuren (08:00–12:00, 13:00–17:00) van Magazijn Antwerpen.",
            warning);
    }

    [Fact]
    public async Task AfterClosing_Warns()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stop = await LocationStopAsync(h, Monday.AddHours(9), Monday.AddHours(18).AddMinutes(30));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Contains("18:30", warning);
        Assert.Contains("buiten de openingsuren", warning);
    }

    [Fact]
    public async Task BetweenIntervals_Warns_MultiIntervalHoursListed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stop = await LocationStopAsync(h, Monday.AddHours(12).AddMinutes(30), Monday.AddHours(14));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Contains("12:30", warning);
        Assert.Contains("08:00–12:00, 13:00–17:00", warning);
    }

    [Fact]
    public async Task ClosedDay_Warns()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stop = await LocationStopAsync(h, Sunday.AddHours(10), Sunday.AddHours(11));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Equal("De geplande laadtijd op zondag valt op een sluitingsdag van Magazijn Antwerpen.", warning);
    }

    [Fact]
    public async Task NoStructuredHours_NoWarning()
    {
        var h = await SeedAsync(withIntervals: false);
        using var _ = h.Db;
        var stop = await LocationStopAsync(h, Sunday.AddHours(3), Sunday.AddHours(4));
        Assert.Null(stop.Warnings);
    }

    [Fact]
    public async Task DateOnlyStop_MidnightEncoding_DoesNotWarn()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // A date without times travels as plannedFrom = 00:00 with plannedTo null (§14 wire
        // encoding); warning about midnight would be pure noise.
        var stop = await LocationStopAsync(h, Sunday, to: null);
        Assert.Null(stop.Warnings);
    }

    [Fact]
    public async Task Warnings_FollowLiveHours_NotTheSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, h.LocationId, Monday.AddHours(9), Monday.AddHours(10)), CancellationToken.None);
        Assert.Null(created.Order!.Stops.Single(s => s.LocationId == h.LocationId).Warnings);
        h.Db.Context.ChangeTracker.Clear();

        // Master hours change AFTER the order: 09:00 now falls before opening → live warning,
        // while the snapshot summary on the stop stays what was agreed.
        var intervals = await h.Db.Context.Set<LocationOpeningInterval>()
            .Where(i => i.LocationId == h.LocationId).ToListAsync();
        foreach (var interval in intervals.Where(i => i.FromTime == new TimeOnly(8, 0)))
        {
            interval.FromTime = new TimeOnly(10, 0);
        }
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var detail = await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None);
        var stop = detail!.Stops.Single(s => s.LocationId == h.LocationId);
        Assert.NotNull(stop.Warnings);
        Assert.Contains("09:00", Assert.Single(stop.Warnings!));
        Assert.Equal("Ma 08:00–12:00, 13:00–17:00", stop.OpeningHoursSummary);
    }
}
