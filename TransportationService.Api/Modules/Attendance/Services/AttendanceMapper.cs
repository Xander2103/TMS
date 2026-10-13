using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;

namespace TransportationService.Api.Modules.Attendance.Services;

/// <summary>Eén sessie-DTO-mapping voor punchservice, correctieservice en HR-weergaven.</summary>
internal static class AttendanceMapper
{
    public static AttendanceSessionDto ToSessionDto(
        AttendanceSession session,
        IEnumerable<AttendanceBreak> allBreaks,
        IEnumerable<AttendanceCorrection> allCorrections,
        IReadOnlyDictionary<Guid, string> correctorNames,
        IReadOnlyDictionary<Guid, string> locationNames,
        DateTime nowUtc)
    {
        var sessionBreaks = allBreaks.Where(b => b.SessionId == session.Id).OrderBy(b => b.StartedAt).ToList();
        var gross = AttendanceCalculator.GrossMinutes(session.ClockInAt, session.ClockOutAt, nowUtc);
        var breakMinutes = AttendanceCalculator.BreakMinutes(sessionBreaks, nowUtc);

        return new AttendanceSessionDto(
            session.Id,
            session.EmployeeId,
            session.ClockInAt,
            session.ClockOutAt,
            session.Status,
            session.ClockInSource,
            session.LocationId,
            session.LocationId is { } locId && locationNames.TryGetValue(locId, out var locName) ? locName : null,
            gross,
            breakMinutes,
            AttendanceCalculator.NetMinutes(gross, breakMinutes),
            session.HasCorrections,
            session.Version,
            sessionBreaks
                .Select(b => new AttendanceBreakDto(b.Id, b.StartedAt, b.EndedAt,
                    AttendanceCalculator.BreakMinutes([b], nowUtc)))
                .ToList(),
            allCorrections.Where(c => c.SessionId == session.Id)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new AttendanceCorrectionDto(
                    c.Id, c.Kind, c.BreakId, c.OldValue, c.NewValue, c.Reason,
                    c.CreatedByUserId is { } uid && correctorNames.TryGetValue(uid, out var name) ? name : null,
                    c.CreatedAt))
                .ToList());
    }
}
