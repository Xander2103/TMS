using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Common;

/// <summary>
/// THE one transport-time convention (C-03), in one place.
/// <para>
/// Every operational timestamp is stored and transported as a <b>UTC instant</b>. Everything a
/// human types or reads — an opening-hours interval, a time on a package label, the calendar day
/// a document run belongs to — is <b>tenant wall clock</b>, driven by
/// <c>TenantSettings.Timezone</c>. Any code that compares, formats or truncates an instant against
/// something human-facing must pass through this class first; comparing the raw instant is the
/// C-03 defect.
/// </para>
/// <para>
/// This is the single resolver in the API: <c>AttendanceCalculator.ResolveTimeZone</c> delegates
/// here, so attendance, orders, labels, planning proposals and document runs cannot drift apart
/// on the fallback policy.
/// </para>
/// </summary>
public static class TenantTimeZone
{
    /// <summary>Tenant default (entity default, <c>MasterDataSeeder</c> and the web client's own
    /// fallback in <c>utils/dates.ts</c>). Everything degrades to this, never to UTC, so a
    /// backend verdict can never contradict the clock rendered next to it.</summary>
    public const string DefaultId = "Europe/Amsterdam";

    /// <summary>
    /// IANA id → <see cref="TimeZoneInfo"/>. Never throws. The setting is unvalidated free text
    /// (an operator can type "Brussel"), so an empty or unknown id degrades to
    /// <see cref="DefaultId"/>. Only a runtime with no time-zone database at all falls back to
    /// UTC — resolving a zone must never take down a request.
    /// </summary>
    public static TimeZoneInfo Resolve(string? ianaTimeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(ianaTimeZoneId) ? DefaultId : ianaTimeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Fall through to the tenant default below.
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// The tenant's zone, read from <c>tenant_settings</c>. A tenant without a settings row (the
    /// row is created lazily by <c>CompanySettingsService</c>) resolves to <see cref="DefaultId"/>,
    /// exactly like an unknown id — the SQL data migrations mirror this with a LEFT JOIN + COALESCE.
    /// </summary>
    public static async Task<TimeZoneInfo> ForTenantAsync(
        TransportationDbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
    {
        var id = await dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(cancellationToken);
        return Resolve(id);
    }

    /// <summary>
    /// UTC instant → tenant wall clock. Accepts any <see cref="DateTime.Kind"/>: stored values
    /// arrive as <c>Utc</c> from Npgsql and as <c>Unspecified</c> from an entity that has not
    /// round-tripped (and from the SQLite test provider); both denote the same instant. The API
    /// never produces <c>Local</c> values.
    /// </summary>
    public static DateTime ToWallClock(DateTime instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc), zone);

    /// <inheritdoc cref="ToWallClock(DateTime, TimeZoneInfo)"/>
    public static DateTime? ToWallClock(DateTime? instant, TimeZoneInfo zone) =>
        instant is { } value ? ToWallClock(value, zone) : null;

    /// <summary>The tenant-local calendar day an instant belongs to. <c>DateOnly.FromDateTime</c>
    /// on the raw instant puts an early-morning stop on the previous day.</summary>
    public static DateOnly ToLocalDate(DateTime instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(ToWallClock(instant, zone));

    /// <summary>
    /// MD-M3 (master-data acceptance 2026-10-18): "today" as the TENANT's calendar day. After local
    /// midnight <c>DateTime.UtcNow.Date</c> is still yesterday in UTC — an invoice date, an
    /// employee end date or an order date defaulted to the previous day. Use this, not UTC.
    /// </summary>
    public static async Task<DateOnly> TodayAsync(
        TransportationDbContext dbContext, Guid tenantId, TimeProvider clock, CancellationToken cancellationToken) =>
        ToLocalDate(clock.GetUtcNow().UtcDateTime, await ForTenantAsync(dbContext, tenantId, cancellationToken));

    /// <summary>
    /// Tenant wall clock → UTC instant: the inverse of <see cref="ToWallClock(DateTime, TimeZoneInfo)"/>,
    /// for everything a human types as a local date and time. DST-safe: a time inside the
    /// spring-forward gap shifts past it, an ambiguous autumn time takes its first occurrence.
    /// Always <see cref="DateTimeKind.Utc"/>, which is what a <c>timestamp with time zone</c>
    /// parameter requires — Npgsql refuses a <c>Kind=Unspecified</c> value outright.
    /// </summary>
    public static DateTime ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }
        else if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            return DateTime.SpecifyKind(local - offsets.Max(), DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>
    /// The UTC window <c>[StartUtc, EndUtc)</c> covering the inclusive tenant-local day range
    /// <paramref name="from"/>..<paramref name="to"/>: what a query over an instant column must
    /// compare against for a date filter typed by a human ("van 20/07 tot 24/07"). Comparing
    /// <c>from.ToDateTime(TimeOnly.MinValue)</c> instead is the C-03 defect twice over — the
    /// bounds are off by the tenant offset AND Npgsql rejects their <c>Kind=Unspecified</c>.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        (ToUtc(from, TimeOnly.MinValue, zone), ToUtc(to.AddDays(1), TimeOnly.MinValue, zone));
}
