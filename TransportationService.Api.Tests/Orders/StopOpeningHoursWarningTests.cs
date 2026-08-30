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
///
/// C-03 (one transport-time convention): a stop's planned window is stored as a UTC INSTANT,
/// while a location's opening hours are LOCAL wall clock. The comparison therefore runs in the
/// tenant zone (<c>TenantSettings.Timezone</c>). The cases below come in two families:
/// <list type="bullet">
/// <item>a tenant on <c>UTC</c>, where instant == wall clock — these pin the evaluator, the
/// message text and the date-only rule without any zone arithmetic in play;</item>
/// <item>a tenant on <c>Europe/Amsterdam</c>, where they differ by one or two hours — these pin
/// the projection itself, including the weekday and the date-only definition.</item>
/// </list>
/// </summary>
public class StopOpeningHoursWarningTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    // 2026-07-20 is a Monday; the seeded location opens Ma 08:00–12:00 and 13:00–17:00.
    private static readonly DateTime Monday = new(2026, 7, 20);
    private static readonly DateTime Sunday = new(2026, 7, 19);

    /// <summary>An instant on the wire, i.e. what the database actually holds.</summary>
    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync(bool withIntervals = true, string timezone = "UTC")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Timezone = timezone,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
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

    // ------------------------------------------------------------------
    // Tenant on UTC: the wire instant IS the wall clock, so these cases
    // exercise the evaluator, the message text and the date-only rule.
    // ------------------------------------------------------------------

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
        // A date without times travels as plannedFrom = LOCAL 00:00 with plannedTo null (§14 wire
        // encoding); on a UTC tenant that is 00:00Z. Warning about midnight would be pure noise.
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

    // ------------------------------------------------------------------
    // Tenant on Europe/Amsterdam: instant != wall clock. These pin the
    // C-03 projection — hour, weekday and the date-only definition.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Summer_EightAmLocal_IsInsideOpeningHours_NoWarning()
    {
        var h = await SeedAsync(timezone: "Europe/Amsterdam");
        using var _ = h.Db;
        // A dispatcher typed 08:00–10:00 on Monday 20 July; CEST is +02:00, so the wire holds
        // 06:00Z–08:00Z. Compared as raw UTC that reads "before opening" (see the paired case).
        var stop = await LocationStopAsync(h, Utc(2026, 7, 20, 6), Utc(2026, 7, 20, 8));
        Assert.Null(stop.Warnings);
    }

    [Fact]
    public async Task Summer_SameInstant_OnAUtcTenant_StillWarnsBeforeOpening()
    {
        var h = await SeedAsync(timezone: "UTC");
        using var _ = h.Db;
        // Paired with the case above: identical stored instants, only the tenant zone differs.
        // 06:00Z IS 06:00 wall clock here, so the warning is correct and must survive.
        var stop = await LocationStopAsync(h, Utc(2026, 7, 20, 6), Utc(2026, 7, 20, 8));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Equal(
            "De geplande laadtijd van 06:00 valt buiten de openingsuren (08:00–12:00, 13:00–17:00) van Magazijn Antwerpen.",
            warning);
    }

    [Fact]
    public async Task Winter_EightAmLocal_IsInsideOpeningHours_NoWarning()
    {
        var h = await SeedAsync(timezone: "Europe/Amsterdam");
        using var _ = h.Db;
        // Monday 19 January 2026, CET is +01:00: 08:00–10:00 local is 07:00Z–09:00Z. The offset
        // is not a constant, so the projection has to be date-aware.
        var stop = await LocationStopAsync(h, Utc(2026, 1, 19, 7), Utc(2026, 1, 19, 9));
        Assert.Null(stop.Warnings);
    }

    [Fact]
    public async Task Summer_EveningOutsideHours_ReportsTheLocalTime()
    {
        var h = await SeedAsync(timezone: "Europe/Amsterdam");
        using var _ = h.Db;
        // 11:00–18:30 local on Monday 20 July = 09:00Z–16:30Z. The message must name 18:30,
        // the hour the dispatcher sees, not 16:30.
        var stop = await LocationStopAsync(h, Utc(2026, 7, 20, 9), Utc(2026, 7, 20, 16, 30));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Equal(
            "De geplande laadtijd van 18:30 valt buiten de openingsuren (08:00–12:00, 13:00–17:00) van Magazijn Antwerpen.",
            warning);
    }

    [Fact]
    public async Task DateOnlyStop_IsLocalMidnight_NotUtcMidnight()
    {
        var h = await SeedAsync(timezone: "Europe/Amsterdam");
        using var _ = h.Db;
        // A date-only stop on Monday 20 July travels as 00:00 Amsterdam = 2026-07-19T22:00:00Z.
        // The date-only guard has to recognise it by its LOCAL time of day; a UTC-based guard
        // sees 22:00 on a Sunday and emits a "sluitingsdag zondag" warning out of nothing.
        var stop = await LocationStopAsync(h, Utc(2026, 7, 19, 22), to: null);
        Assert.Null(stop.Warnings);
    }

    [Fact]
    public async Task InstantOnSundayUtc_ButMondayLocal_UsesTheLocalWeekday()
    {
        var h = await SeedAsync(timezone: "Europe/Amsterdam");
        using var _ = h.Db;
        // 00:30–10:00 local on Monday 20 July = 2026-07-19T22:30:00Z – 2026-07-20T08:00:00Z.
        // The "from" is a Sunday in UTC: the weekday must come from the local projection, so the
        // warning is Monday's "outside opening hours", never Sunday's "sluitingsdag".
        var stop = await LocationStopAsync(h, Utc(2026, 7, 19, 22, 30), Utc(2026, 7, 20, 8));
        var warning = Assert.Single(stop.Warnings!);
        Assert.Equal(
            "De geplande laadtijd van 00:30 valt buiten de openingsuren (08:00–12:00, 13:00–17:00) van Magazijn Antwerpen.",
            warning);
        Assert.DoesNotContain("zondag", warning);
    }

    [Fact]
    public async Task UnknownTimezoneId_FallsBackToTheTenantDefault_NotToUtc()
    {
        var h = await SeedAsync(timezone: "Brussel");
        using var _ = h.Db;
        // The timezone setting is free text (an operator can type "Brussel"). The web client
        // degrades such a value to Europe/Amsterdam, so the backend must degrade the same way —
        // otherwise the warning contradicts the clock shown next to it.
        var stop = await LocationStopAsync(h, Utc(2026, 7, 20, 6), Utc(2026, 7, 20, 8));
        Assert.Null(stop.Warnings);
    }
}
