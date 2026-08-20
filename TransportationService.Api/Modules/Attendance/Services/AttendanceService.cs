using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceService
{
    Task<AttendancePunchResult> ClockInAsync(Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken);
    Task<AttendancePunchResult> ClockOutAsync(Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken);
    Task<AttendancePunchResult> StartBreakAsync(Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken);
    Task<AttendancePunchResult> EndBreakAsync(Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken);

    Task<AttendanceStatusDto> GetStatusAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<AttendanceHistoryDto> GetHistoryAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<DriverDaySummaryDto> GetDriverDaySummaryAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>
/// Kern van de urenregistratie: punches met server-side state machine. De databank is
/// de laatste verdedigingslinie tegen races (gefilterde unieke indexen op actieve
/// sessie en open pauze); dubbele verzoeken vertalen zich in nette outcome-fouten, niet
/// in dubbele registraties. Alle tijdstippen komen uit <see cref="TimeProvider"/> (UTC).
/// Deze service bevat bewust GEEN rijtijd-/tachograaflogica en GEEN loonberekening —
/// zie docs/attendance/architecture.md voor die grenzen.
/// </summary>
public class AttendanceService : IAttendanceService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AttendanceService(TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<AttendancePunchResult> ClockInAsync(
        Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken)
    {
        var gate = await CheckPunchPreconditionsAsync(employeeId, context, cancellationToken);
        if (gate is not null)
        {
            return gate;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var active = await ActiveSessionAsync(employeeId, cancellationToken);
        if (!AttendanceStateMachine.CanClockIn(active))
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.AlreadyClockedIn, "Je bent al ingepunt.");
        }

        var session = new AttendanceSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            ClockInAt = now,
            Status = AttendanceSessionStatus.Working,
            ClockInSource = context.Source,
            KioskDeviceId = context.KioskDeviceId,
            LocationId = context.LocationId,
        };
        _dbContext.AttendanceSessions.Add(session);
        AddEvent(session, AttendanceEventType.ClockIn, now, context);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // De unieke index "één actieve sessie per medewerker" ving een race
            // (dubbelklik, tweede browser, netwerkretry): behandel als al ingepunt.
            _dbContext.ChangeTracker.Clear();
            return AttendancePunchResult.Fail(AttendancePunchOutcome.AlreadyClockedIn, "Je bent al ingepunt.");
        }

        return AttendancePunchResult.Ok(await GetStatusAsync(employeeId, cancellationToken));
    }

    public async Task<AttendancePunchResult> ClockOutAsync(
        Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken)
    {
        var gate = await CheckPunchPreconditionsAsync(employeeId, context, cancellationToken);
        if (gate is not null)
        {
            return gate;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var active = await ActiveSessionAsync(employeeId, cancellationToken);
        if (!AttendanceStateMachine.CanClockOut(active))
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.NotClockedIn, "Je bent niet ingepunt.");
        }

        if (active!.Status == AttendanceSessionStatus.OnBreak)
        {
            // Uitpunten tijdens pauze: pauze wordt op hetzelfde moment afgesloten.
            var openBreak = await OpenBreakAsync(active.Id, cancellationToken);
            if (openBreak is not null)
            {
                openBreak.EndedAt = now;
                AddEvent(active, AttendanceEventType.BreakEnded, now, context);
            }
        }

        active.ClockOutAt = now;
        active.ClockOutSource = context.Source;
        active.Status = AttendanceSessionStatus.Completed;
        AddEvent(active, AttendanceEventType.ClockOut, now, context);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Gelijktijdige uitpunt (dubbelklik/tweede browser): de eerste won; geen
            // dubbel ClockOut-event in de timeline.
            _dbContext.ChangeTracker.Clear();
            return AttendancePunchResult.Fail(AttendancePunchOutcome.NotClockedIn, "Je bent niet ingepunt.");
        }

        return AttendancePunchResult.Ok(await GetStatusAsync(employeeId, cancellationToken));
    }

    public async Task<AttendancePunchResult> StartBreakAsync(
        Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken)
    {
        var gate = await CheckPunchPreconditionsAsync(employeeId, context, cancellationToken);
        if (gate is not null)
        {
            return gate;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var active = await ActiveSessionAsync(employeeId, cancellationToken);
        if (active is null)
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.NotClockedIn, "Je bent niet ingepunt.");
        }

        if (!AttendanceStateMachine.CanStartBreak(active))
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.BreakAlreadyActive, "Er loopt al een pauze.");
        }

        _dbContext.AttendanceBreaks.Add(new AttendanceBreak
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            SessionId = active.Id,
            EmployeeId = employeeId,
            StartedAt = now,
        });
        active.Status = AttendanceSessionStatus.OnBreak;
        AddEvent(active, AttendanceEventType.BreakStarted, now, context);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race op de unieke open-pauze-index: er loopt al een pauze.
            _dbContext.ChangeTracker.Clear();
            return AttendancePunchResult.Fail(AttendancePunchOutcome.BreakAlreadyActive, "Er loopt al een pauze.");
        }

        return AttendancePunchResult.Ok(await GetStatusAsync(employeeId, cancellationToken));
    }

    public async Task<AttendancePunchResult> EndBreakAsync(
        Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken)
    {
        var gate = await CheckPunchPreconditionsAsync(employeeId, context, cancellationToken);
        if (gate is not null)
        {
            return gate;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var active = await ActiveSessionAsync(employeeId, cancellationToken);
        if (active is null)
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.NotClockedIn, "Je bent niet ingepunt.");
        }

        var openBreak = await OpenBreakAsync(active.Id, cancellationToken);
        if (!AttendanceStateMachine.CanEndBreak(active) || openBreak is null)
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.NoActiveBreak, "Er loopt geen pauze.");
        }

        openBreak.EndedAt = now;
        active.Status = AttendanceSessionStatus.Working;
        AddEvent(active, AttendanceEventType.BreakEnded, now, context);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return AttendancePunchResult.Fail(AttendancePunchOutcome.NoActiveBreak, "Er loopt geen pauze.");
        }

        return AttendancePunchResult.Ok(await GetStatusAsync(employeeId, cancellationToken));
    }

    /// <summary>
    /// Lichtgewicht statusquery voor dashboard/kiosk: actieve sessie + open pauze +
    /// dagtotalen. Haalt bewust géén volledige eventhistorie op.
    /// </summary>
    public async Task<AttendanceStatusDto> GetStatusAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = await TenantTimeZoneAsync(cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timeZone));
        var (todayStartUtc, todayEndUtc) = AttendanceCalculator.LocalDayWindowUtc(today, timeZone);

        // Sessies die vandaag raken (incl. nachtshift van gisteren die nog loopt).
        var sessions = await Scoped()
            .AsNoTracking()
            .Where(s => s.EmployeeId == employeeId
                        && s.Status != AttendanceSessionStatus.Cancelled
                        && s.ClockInAt < todayEndUtc
                        && (s.ClockOutAt == null || s.ClockOutAt >= todayStartUtc))
            .ToListAsync(cancellationToken);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        List<AttendanceBreak> breaks = sessionIds.Count == 0
            ? []
            : await _dbContext.AttendanceBreaks
                .AsNoTracking()
                .Where(b => b.TenantId == _tenantContext.TenantId && sessionIds.Contains(b.SessionId))
                .ToListAsync(cancellationToken);

        var workedToday = 0;
        var breakToday = 0;
        foreach (var session in sessions)
        {
            var sessionBreaks = breaks.Where(b => b.SessionId == session.Id).ToList();
            var slice = AttendanceCalculator
                .SplitByCalendarDay(session.ClockInAt, session.ClockOutAt, sessionBreaks, timeZone, now)
                .FirstOrDefault(s => s.Day == today);
            if (slice is not null)
            {
                workedToday += slice.NetMinutes;
                breakToday += slice.BreakMinutes;
            }
        }

        var active = sessions.FirstOrDefault(AttendanceStateMachine.IsActive);
        var openBreak = active is null
            ? null
            : breaks.FirstOrDefault(b => b.SessionId == active.Id && b.EndedAt == null);

        var status = active switch
        {
            { Status: AttendanceSessionStatus.OnBreak } => AttendanceLiveStatus.OnBreak,
            { } => AttendanceLiveStatus.Working,
            null when sessions.Any(s => s.ClockOutAt is not null) => AttendanceLiveStatus.ClockedOut,
            _ => AttendanceLiveStatus.NotClockedIn,
        };

        var lastClockOut = sessions.Where(s => s.ClockOutAt is not null)
            .OrderByDescending(s => s.ClockOutAt)
            .Select(s => s.ClockOutAt)
            .FirstOrDefault();

        return new AttendanceStatusDto(
            status,
            active?.Id,
            active?.ClockInAt,
            openBreak?.StartedAt,
            lastClockOut,
            workedToday,
            breakToday,
            CanClockIn: AttendanceStateMachine.CanClockIn(active),
            CanClockOut: AttendanceStateMachine.CanClockOut(active),
            CanStartBreak: AttendanceStateMachine.CanStartBreak(active),
            CanEndBreak: AttendanceStateMachine.CanEndBreak(active));
    }

    /// <summary>
    /// Historie per kalenderdag (tenant-tijdzone) met sessies, pauzes, correcties en
    /// geplande minuten uit de Employee Planning (Work + Training; Standby telt niet als
    /// geplande werktijd). Planning en attendance blijven aparte bronnen: gepland is
    /// verwacht, attendance is werkelijk geregistreerd.
    /// </summary>
    public async Task<AttendanceHistoryDto> GetHistoryAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = await TenantTimeZoneAsync(cancellationToken);
        var (rangeStartUtc, _) = AttendanceCalculator.LocalDayWindowUtc(from, timeZone);
        var (_, rangeEndUtc) = AttendanceCalculator.LocalDayWindowUtc(to, timeZone);

        var sessions = await Scoped()
            .AsNoTracking()
            .Where(s => s.EmployeeId == employeeId
                        && s.ClockInAt < rangeEndUtc
                        && (s.ClockOutAt == null || s.ClockOutAt >= rangeStartUtc))
            .OrderBy(s => s.ClockInAt)
            .ToListAsync(cancellationToken);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        List<AttendanceBreak> breaks = sessionIds.Count == 0
            ? []
            : await _dbContext.AttendanceBreaks
                .AsNoTracking()
                .Where(b => b.TenantId == _tenantContext.TenantId && sessionIds.Contains(b.SessionId))
                .OrderBy(b => b.StartedAt)
                .ToListAsync(cancellationToken);

        List<AttendanceCorrection> corrections = sessionIds.Count == 0
            ? []
            : await _dbContext.AttendanceCorrections
                .AsNoTracking()
                .Where(c => c.TenantId == _tenantContext.TenantId && sessionIds.Contains(c.SessionId))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(cancellationToken);

        var correctorIds = corrections.Where(c => c.CreatedByUserId is not null)
            .Select(c => c.CreatedByUserId!.Value)
            .Distinct()
            .ToList();
        var correctorNames = correctorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId && correctorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var locationIds = sessions.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList();
        var locationNames = locationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var plannedByDay = await _dbContext.Shifts
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId
                        && s.EmployeeId == employeeId
                        && s.Date >= from && s.Date <= to
                        && s.Type != ShiftType.Standby)
            .Select(s => new { s.Date, s.StartTime, s.EndTime, s.BreakMinutes })
            .ToListAsync(cancellationToken);
        var plannedMinutes = plannedByDay
            .GroupBy(s => s.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(s => Math.Max(0, (int)(s.EndTime - s.StartTime).TotalMinutes - s.BreakMinutes)));

        var days = new List<AttendanceDayDto>();
        var sliceIndex = new Dictionary<DateOnly, List<(AttendanceSession Session, AttendanceCalculator.DaySlice Slice)>>();
        foreach (var session in sessions.Where(s => s.Status != AttendanceSessionStatus.Cancelled))
        {
            var sessionBreaks = breaks.Where(b => b.SessionId == session.Id).ToList();
            foreach (var slice in AttendanceCalculator.SplitByCalendarDay(
                         session.ClockInAt, session.ClockOutAt, sessionBreaks, timeZone, now))
            {
                if (slice.Day < from || slice.Day > to)
                {
                    continue;
                }

                if (!sliceIndex.TryGetValue(slice.Day, out var list))
                {
                    sliceIndex[slice.Day] = list = [];
                }

                list.Add((session, slice));
            }
        }

        var allDays = sliceIndex.Keys.Union(plannedMinutes.Keys).Where(d => d >= from && d <= to).OrderBy(d => d);
        foreach (var day in allDays)
        {
            List<(AttendanceSession Session, AttendanceCalculator.DaySlice Slice)> daySlices =
                sliceIndex.TryGetValue(day, out var list) ? list : [];
            var sessionDtos = daySlices
                .Select(x => x.Session)
                .Distinct()
                .OrderBy(s => s.ClockInAt)
                .Select(s => AttendanceMapper.ToSessionDto(s, breaks, corrections, correctorNames, locationNames, now))
                .ToList();

            days.Add(new AttendanceDayDto(
                day,
                daySlices.Sum(x => x.Slice.GrossMinutes),
                daySlices.Sum(x => x.Slice.BreakMinutes),
                daySlices.Sum(x => x.Slice.NetMinutes),
                plannedMinutes.TryGetValue(day, out var planned) ? planned : null,
                sessionDtos));
        }

        return new AttendanceHistoryDto(
            from,
            to,
            days.Sum(d => d.GrossMinutes),
            days.Sum(d => d.BreakMinutes),
            days.Sum(d => d.NetMinutes),
            plannedMinutes.Count == 0 ? null : days.Sum(d => d.PlannedMinutes ?? 0),
            days);
    }

    /// <summary>
    /// Read-model voor de Driver Activity Card: attendance + planning van vandaag als
    /// strikt gescheiden bronnen. Tachograafdata bestaat hier bewust alleen als
    /// "niet gekoppeld"-vlag tot een echte providerintegratie (Modules/DriverActivity)
    /// bestaat — attendance is nooit een bron voor wettelijke rij-/rusttijden.
    /// </summary>
    public async Task<DriverDaySummaryDto> GetDriverDaySummaryAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(employeeId, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = await TenantTimeZoneAsync(cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timeZone));

        var planned = await _dbContext.Shifts
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId
                        && s.EmployeeId == employeeId
                        && s.Date == today
                        && s.Type != ShiftType.Standby)
            .OrderBy(s => s.StartTime)
            .Select(s => new DriverDayPlanDto(s.StartTime, s.EndTime, s.BreakMinutes, s.RoleLabel))
            .ToListAsync(cancellationToken);

        return new DriverDaySummaryDto(status, planned, TachographConnected: false);
    }

    // ── Intern ───────────────────────────────────────────────────────────────────

    private IQueryable<AttendanceSession> Scoped() =>
        _dbContext.AttendanceSessions.Where(s => s.TenantId == _tenantContext.TenantId);

    private Task<AttendanceSession?> ActiveSessionAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Scoped().FirstOrDefaultAsync(
            s => s.EmployeeId == employeeId && s.ClockOutAt == null
                 && (s.Status == AttendanceSessionStatus.Working || s.Status == AttendanceSessionStatus.OnBreak),
            cancellationToken);

    private Task<AttendanceBreak?> OpenBreakAsync(Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.AttendanceBreaks.FirstOrDefaultAsync(
            b => b.TenantId == _tenantContext.TenantId && b.SessionId == sessionId && b.EndedAt == null,
            cancellationToken);

    private async Task<AttendancePunchResult?> CheckPunchPreconditionsAsync(
        Guid employeeId, AttendancePunchContext context, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId)
            .Select(e => new { e.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.EmployeeNotFound, "Medewerker niet gevonden.");
        }

        if (!employee.IsActive)
        {
            return AttendancePunchResult.Fail(
                AttendancePunchOutcome.EmployeeInactive, "Urenregistratie is niet mogelijk voor een inactieve medewerker.");
        }

        var settings = await SettingsSnapshotAsync(cancellationToken);
        if (context.Source is AttendanceSource.Web or AttendanceSource.Mobile && !settings.SelfPunchEnabled)
        {
            return AttendancePunchResult.Fail(
                AttendancePunchOutcome.SelfPunchDisabled, "Zelf in-/uitpunten is uitgeschakeld. Gebruik de prikklok.");
        }

        if (context.Source == AttendanceSource.Kiosk && !settings.KioskEnabled)
        {
            return AttendancePunchResult.Fail(AttendancePunchOutcome.KioskDisabled, "De prikklok is uitgeschakeld.");
        }

        return null;
    }

    private void AddEvent(AttendanceSession session, AttendanceEventType type, DateTime occurredAt, AttendancePunchContext context)
    {
        _dbContext.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            SessionId = session.Id,
            EmployeeId = session.EmployeeId,
            EventType = type,
            OccurredAt = occurredAt,
            Source = context.Source,
            KioskDeviceId = context.KioskDeviceId,
            LocationId = context.LocationId,
        });
    }

    private async Task<TimeZoneInfo> TenantTimeZoneAsync(CancellationToken cancellationToken)
    {
        var timezone = await _dbContext.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(cancellationToken);
        return AttendanceCalculator.ResolveTimeZone(timezone);
    }

    private async Task<(bool SelfPunchEnabled, bool KioskEnabled)> SettingsSnapshotAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings
            .AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => new { s.SelfPunchEnabled, s.KioskEnabled })
            .FirstOrDefaultAsync(cancellationToken);
        return settings is null ? (true, true) : (settings.SelfPunchEnabled, settings.KioskEnabled);
    }
}
