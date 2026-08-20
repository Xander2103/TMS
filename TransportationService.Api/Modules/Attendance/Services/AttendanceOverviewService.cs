using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceOverviewService
{
    Task<AttendanceOverviewDto> GetOverviewAsync(
        DateOnly? date, Guid? departmentId, string? search, CancellationToken cancellationToken);
}

/// <summary>
/// HR-liveoverzicht "Aanwezigheid vandaag" (spec §16/§17). Metriekdefinities:
///   • Status per medewerker, in aflopende prioriteit: ForgottenClockOut (actieve sessie
///     langer dan de tenant-instelling), OnBreak, Working, ClockedOut (vandaag gewerkt en
///     uitgepunt), Absent (goedgekeurde afwezigheid vandaag — inpunten wordt niet
///     verwacht, §50), NotClockedIn.
///   • WorkedMinutes/BreakMinutes: het dagdeel (tenant-tijdzone) van alle sessies die de
///     gevraagde dag raken — een nachtshift telt dus alleen het deel binnen die dag mee.
///   • PlannedNotClockedIn: alleen voor "vandaag": er is een geplande shift (Work of
///     Training), de geplande start + grace-marge is verstreken, er is geen registratie
///     én geen goedgekeurde afwezigheid (§51).
/// Alleen actieve medewerkers verschijnen; het overzicht toont de werkelijke registratie
/// en verandert nooit iets aan de planning (aparte bronnen, §19).
/// </summary>
public class AttendanceOverviewService : IAttendanceOverviewService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AttendanceOverviewService(
        TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<AttendanceOverviewDto> GetOverviewAsync(
        DateOnly? date, Guid? departmentId, string? search, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var settings = await _dbContext.AttendanceSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new { s.ForgottenClockOutAfterHours, s.PlannedNotClockedInGraceMinutes })
            .FirstOrDefaultAsync(cancellationToken);
        var forgottenAfter = TimeSpan.FromHours(settings?.ForgottenClockOutAfterHours ?? 16);
        var grace = TimeSpan.FromMinutes(settings?.PlannedNotClockedInGraceMinutes ?? 30);

        var timezoneId = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(cancellationToken);
        var timeZone = AttendanceCalculator.ResolveTimeZone(timezoneId);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timeZone));
        var day = date ?? today;
        var isToday = day == today;
        var (dayStartUtc, dayEndUtc) = AttendanceCalculator.LocalDayWindowUtc(day, timeZone);

        var employeeQuery = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive);
        if (departmentId is { } dept)
        {
            employeeQuery = employeeQuery.Where(e => e.DepartmentId == dept);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            employeeQuery = employeeQuery.Where(e =>
                (e.FirstName + " " + e.LastName).ToLower().Contains(term)
                || e.EmployeeNumber.ToLower().Contains(term));
        }

        var employees = await employeeQuery
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.DepartmentId })
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync(cancellationToken);
        var employeeIds = employees.Select(e => e.Id).ToList();

        var departmentNames = await _dbContext.Departments.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var sessions = await _dbContext.AttendanceSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && employeeIds.Contains(s.EmployeeId)
                        && s.Status != AttendanceSessionStatus.Cancelled
                        && s.ClockInAt < dayEndUtc
                        && (s.ClockOutAt == null || s.ClockOutAt >= dayStartUtc))
            .ToListAsync(cancellationToken);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        List<AttendanceBreak> breaks = sessionIds.Count == 0
            ? []
            : await _dbContext.AttendanceBreaks.AsNoTracking()
                .Where(b => b.TenantId == tenantId && sessionIds.Contains(b.SessionId))
                .ToListAsync(cancellationToken);

        var shifts = await _dbContext.Shifts.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Date == day
                        && employeeIds.Contains(s.EmployeeId)
                        && s.Type != ShiftType.Standby)
            .Select(s => new { s.EmployeeId, s.StartTime, s.EndTime, s.BreakMinutes })
            .ToListAsync(cancellationToken);

        var absences = await _dbContext.Absences.AsNoTracking()
            .Where(a => a.TenantId == tenantId
                        && employeeIds.Contains(a.EmployeeId)
                        && a.Status == AbsenceStatus.Approved
                        && a.StartDate <= day && a.EndDate >= day)
            .Select(a => new { a.EmployeeId, a.Type })
            .ToListAsync(cancellationToken);
        var absenceByEmployee = absences
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().Type);

        var rows = new List<AttendanceOverviewRowDto>();
        int working = 0, onBreak = 0, clockedOut = 0, notClockedIn = 0, plannedNot = 0, forgotten = 0, absent = 0;

        foreach (var employee in employees)
        {
            var employeeSessions = sessions.Where(s => s.EmployeeId == employee.Id).ToList();
            var workedMinutes = 0;
            var breakMinutes = 0;
            foreach (var session in employeeSessions)
            {
                var sessionBreaks = breaks.Where(b => b.SessionId == session.Id).ToList();
                var slice = AttendanceCalculator
                    .SplitByCalendarDay(session.ClockInAt, session.ClockOutAt, sessionBreaks, timeZone, now)
                    .FirstOrDefault(s => s.Day == day);
                if (slice is not null)
                {
                    workedMinutes += slice.NetMinutes;
                    breakMinutes += slice.BreakMinutes;
                }
            }

            var active = employeeSessions.FirstOrDefault(AttendanceStateMachine.IsActive);
            var employeeShifts = shifts.Where(s => s.EmployeeId == employee.Id).ToList();
            int? plannedMinutes = employeeShifts.Count == 0
                ? null
                : employeeShifts.Sum(s => Math.Max(0, (int)(s.EndTime - s.StartTime).TotalMinutes - s.BreakMinutes));
            var plannedStart = employeeShifts.Count == 0 ? (TimeOnly?)null : employeeShifts.Min(s => s.StartTime);
            var plannedEnd = employeeShifts.Count == 0 ? (TimeOnly?)null : employeeShifts.Max(s => s.EndTime);
            var hasAbsence = absenceByEmployee.TryGetValue(employee.Id, out var absenceType);

            AttendanceOverviewStatus status;
            DateTime? since = null;
            if (active is not null)
            {
                var openBreak = breaks.FirstOrDefault(b => b.SessionId == active.Id && b.EndedAt == null);
                if (now - active.ClockInAt > forgottenAfter)
                {
                    status = AttendanceOverviewStatus.ForgottenClockOut;
                    since = active.ClockInAt;
                    forgotten++;
                }
                else if (active.Status == AttendanceSessionStatus.OnBreak)
                {
                    status = AttendanceOverviewStatus.OnBreak;
                    since = openBreak?.StartedAt ?? active.ClockInAt;
                    onBreak++;
                }
                else
                {
                    status = AttendanceOverviewStatus.Working;
                    since = active.ClockInAt;
                    working++;
                }
            }
            else if (employeeSessions.Any(s => s.ClockOutAt is not null))
            {
                status = AttendanceOverviewStatus.ClockedOut;
                since = employeeSessions.Where(s => s.ClockOutAt is not null).Max(s => s.ClockOutAt);
                clockedOut++;
            }
            else if (hasAbsence)
            {
                status = AttendanceOverviewStatus.Absent;
                absent++;
            }
            else
            {
                status = AttendanceOverviewStatus.NotClockedIn;
                notClockedIn++;
            }

            // Anomalie "gepland maar niet ingepunt": alleen live (vandaag) zinvol.
            var plannedNotClockedIn = false;
            if (isToday
                && status == AttendanceOverviewStatus.NotClockedIn
                && plannedStart is { } start)
            {
                var plannedStartUtc = AttendanceCalculator.LocalDayWindowUtc(day, timeZone).StartUtc
                    .Add(start.ToTimeSpan());
                if (now > plannedStartUtc.Add(grace))
                {
                    plannedNotClockedIn = true;
                    plannedNot++;
                }
            }

            rows.Add(new AttendanceOverviewRowDto(
                employee.Id,
                $"{employee.FirstName} {employee.LastName}".Trim(),
                employee.EmployeeNumber,
                employee.DepartmentId is { } deptId && departmentNames.TryGetValue(deptId, out var deptName) ? deptName : null,
                status,
                since,
                workedMinutes,
                breakMinutes,
                plannedMinutes,
                plannedStart,
                plannedEnd,
                plannedNotClockedIn,
                employeeSessions.Any(s => s.HasCorrections),
                hasAbsence ? AbsenceLabel(absenceType) : null));
        }

        return new AttendanceOverviewDto(
            day,
            new AttendanceOverviewSummaryDto(working, onBreak, clockedOut, notClockedIn, plannedNot, forgotten, absent),
            rows);
    }

    private static string AbsenceLabel(AbsenceType type) => type switch
    {
        AbsenceType.Vacation => "Verlof",
        AbsenceType.Sick => "Ziek",
        AbsenceType.Training => "Opleiding",
        AbsenceType.PersonalLeave => "Persoonlijk verlof",
        AbsenceType.Unpaid => "Onbetaald",
        _ => "Afwezig",
    };
}
