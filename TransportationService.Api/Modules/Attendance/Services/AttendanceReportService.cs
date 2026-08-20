using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceReportService
{
    Task<AttendanceReportDto> BuildAsync(
        DateOnly from, DateOnly to, Guid? employeeId, Guid? departmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Attendance-rapportage per medewerker per kalenderdag. Metriekdefinities:
///   • Dagattributie in de tenant-tijdzone: een nachtshift 22:00–06:00 levert twee
///     dagrijen op; de som van de delen is exact de werkelijke duur (DST-veilig).
///   • GrossMinutes = sessieduur binnen de dag; BreakMinutes = pauzeduur binnen de dag;
///     NetMinutes = Gross − Break (nooit negatief). Lopende sessies tellen tot "nu".
///   • PlannedMinutes = som van geplande shifts (Work + Training; Standby is geen
///     geplande werktijd) van die dag: (einde − begin) − pauzeminuten. Planning is
///     wall-clock en blijft een aparte bron — het rapport vergelijkt, muteert nooit.
///   • DeviationMinutes = Net − Planned, alleen wanneer er planning bestaat.
///   • MissingClockOut = de dag bevat een sessie zonder uitpunt (nog actief of vergeten).
///   • CorrectionCount = aantal correctierijen op sessies die de dag raken.
/// Geannuleerde sessies tellen nergens mee. Dagen zonder registratie én zonder planning
/// verschijnen niet als rij. Dit rapport is attendance-werkelijkheid — het is expliciet
/// GEEN loonberekening (§36/§82) en GEEN bron voor wettelijke rij-/rusttijden (§83).
/// </summary>
public class AttendanceReportService : IAttendanceReportService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AttendanceReportService(
        TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<AttendanceReportDto> BuildAsync(
        DateOnly from, DateOnly to, Guid? employeeId, Guid? departmentId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var timezoneId = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(cancellationToken);
        var timeZone = AttendanceCalculator.ResolveTimeZone(timezoneId);
        var (rangeStartUtc, _) = AttendanceCalculator.LocalDayWindowUtc(from, timeZone);
        var (_, rangeEndUtc) = AttendanceCalculator.LocalDayWindowUtc(to, timeZone);

        var employeeQuery = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId);
        if (employeeId is { } emp)
        {
            employeeQuery = employeeQuery.Where(e => e.Id == emp);
        }

        if (departmentId is { } dept)
        {
            employeeQuery = employeeQuery.Where(e => e.DepartmentId == dept);
        }

        var employees = await employeeQuery
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.DepartmentId })
            .ToListAsync(cancellationToken);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var employeeById = employees.ToDictionary(e => e.Id);

        var departmentNames = await _dbContext.Departments.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var sessions = await _dbContext.AttendanceSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && employeeIds.Contains(s.EmployeeId)
                        && s.Status != AttendanceSessionStatus.Cancelled
                        && s.ClockInAt < rangeEndUtc
                        && (s.ClockOutAt == null || s.ClockOutAt >= rangeStartUtc))
            .ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(s => s.Id).ToList();

        List<AttendanceBreak> breaks = sessionIds.Count == 0
            ? []
            : await _dbContext.AttendanceBreaks.AsNoTracking()
                .Where(b => b.TenantId == tenantId && sessionIds.Contains(b.SessionId))
                .ToListAsync(cancellationToken);

        var correctionCounts = sessionIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _dbContext.AttendanceCorrections.AsNoTracking()
                .Where(c => c.TenantId == tenantId && sessionIds.Contains(c.SessionId))
                .GroupBy(c => c.SessionId)
                .Select(g => new { SessionId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.SessionId, g => g.Count, cancellationToken);

        var planned = await _dbContext.Shifts.AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && employeeIds.Contains(s.EmployeeId)
                        && s.Date >= from && s.Date <= to
                        && s.Type != ShiftType.Standby)
            .Select(s => new { s.EmployeeId, s.Date, s.StartTime, s.EndTime, s.BreakMinutes })
            .ToListAsync(cancellationToken);
        var plannedByEmployeeDay = planned
            .GroupBy(s => (s.EmployeeId, s.Date))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(s => Math.Max(0, (int)(s.EndTime - s.StartTime).TotalMinutes - s.BreakMinutes)));

        // Per (employee, dag) accumuleren.
        var accumulator = new Dictionary<(Guid EmployeeId, DateOnly Day), (int Gross, int Break, bool MissingOut, int Corrections)>();
        foreach (var session in sessions)
        {
            var sessionBreaks = breaks.Where(b => b.SessionId == session.Id).ToList();
            var corrections = correctionCounts.TryGetValue(session.Id, out var count) ? count : 0;
            var slices = AttendanceCalculator.SplitByCalendarDay(
                session.ClockInAt, session.ClockOutAt, sessionBreaks, timeZone, now);
            foreach (var slice in slices)
            {
                if (slice.Day < from || slice.Day > to)
                {
                    continue;
                }

                var key = (session.EmployeeId, slice.Day);
                var current = accumulator.TryGetValue(key, out var value) ? value : (0, 0, false, 0);
                accumulator[key] = (
                    current.Item1 + slice.GrossMinutes,
                    current.Item2 + slice.BreakMinutes,
                    current.Item3 || session.ClockOutAt is null,
                    current.Item4 + corrections);
            }
        }

        // Ook geplande dagen zonder registratie tonen (ontbrekende inpunt zichtbaar maken).
        foreach (var key in plannedByEmployeeDay.Keys.Where(k => !accumulator.ContainsKey(k)))
        {
            accumulator[key] = (0, 0, false, 0);
        }

        var rows = accumulator
            .OrderBy(kv => employeeById[kv.Key.EmployeeId].LastName)
            .ThenBy(kv => employeeById[kv.Key.EmployeeId].FirstName)
            .ThenBy(kv => kv.Key.Day)
            .Select(kv =>
            {
                var employee = employeeById[kv.Key.EmployeeId];
                var plannedMinutes = plannedByEmployeeDay.TryGetValue(kv.Key, out var p) ? (int?)p : null;
                var net = AttendanceCalculator.NetMinutes(kv.Value.Gross, kv.Value.Break);
                return new AttendanceReportRowDto(
                    employee.Id,
                    $"{employee.FirstName} {employee.LastName}".Trim(),
                    employee.EmployeeNumber,
                    employee.DepartmentId is { } deptId && departmentNames.TryGetValue(deptId, out var deptName) ? deptName : null,
                    kv.Key.Day,
                    kv.Value.Gross,
                    kv.Value.Break,
                    net,
                    plannedMinutes,
                    plannedMinutes is { } pm ? net - pm : null,
                    kv.Value.MissingOut,
                    kv.Value.Corrections);
            })
            .ToList();

        return new AttendanceReportDto(
            from,
            to,
            rows.Sum(r => r.GrossMinutes),
            rows.Sum(r => r.BreakMinutes),
            rows.Sum(r => r.NetMinutes),
            rows.Sum(r => r.PlannedMinutes ?? 0),
            rows);
    }
}
