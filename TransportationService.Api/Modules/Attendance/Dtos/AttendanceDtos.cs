using TransportationService.Api.Modules.Attendance.Entities;

namespace TransportationService.Api.Modules.Attendance.Dtos;

// ── Punchen & status ─────────────────────────────────────────────────────────────

/// <summary>Actuele werkstatus van één medewerker — licht genoeg voor dashboardpolling.</summary>
public sealed record AttendanceStatusDto(
    AttendanceLiveStatus Status,
    Guid? SessionId,
    DateTime? ClockInAt,
    DateTime? BreakStartedAt,
    DateTime? LastClockOutAt,
    int WorkedMinutesToday,
    int BreakMinutesToday,
    bool CanClockIn,
    bool CanClockOut,
    bool CanStartBreak,
    bool CanEndBreak);

public enum AttendanceLiveStatus
{
    NotClockedIn,
    Working,
    OnBreak,
    ClockedOut,
}

public enum AttendancePunchOutcome
{
    Success,
    AlreadyClockedIn,
    NotClockedIn,
    BreakAlreadyActive,
    NoActiveBreak,
    EmployeeInactive,
    EmployeeNotFound,
    SelfPunchDisabled,
    KioskDisabled,
}

public sealed record AttendancePunchResult(
    AttendancePunchOutcome Outcome,
    AttendanceStatusDto? Status,
    string? Error)
{
    public static AttendancePunchResult Ok(AttendanceStatusDto status) => new(AttendancePunchOutcome.Success, status, null);
    public static AttendancePunchResult Fail(AttendancePunchOutcome outcome, string error) => new(outcome, null, error);
}

/// <summary>Context van een punch: kanaal + (voor kiosk) device en bronlocatie.</summary>
public sealed record AttendancePunchContext(
    AttendanceSource Source,
    Guid? KioskDeviceId = null,
    Guid? LocationId = null);

// ── Historie & sessies ───────────────────────────────────────────────────────────

public sealed record AttendanceBreakDto(Guid Id, DateTime StartedAt, DateTime? EndedAt, int Minutes);

public sealed record AttendanceCorrectionDto(
    Guid Id,
    AttendanceCorrectionKind Kind,
    Guid? BreakId,
    DateTime? OldValue,
    DateTime? NewValue,
    string Reason,
    string? CorrectedByName,
    DateTime CorrectedAt);

public sealed record AttendanceEventDto(
    Guid Id,
    AttendanceEventType EventType,
    DateTime OccurredAt,
    AttendanceSource Source,
    string? Note,
    Guid? CorrectionId);

public sealed record AttendanceSessionDto(
    Guid Id,
    Guid EmployeeId,
    DateTime ClockInAt,
    DateTime? ClockOutAt,
    AttendanceSessionStatus Status,
    AttendanceSource ClockInSource,
    Guid? LocationId,
    string? LocationName,
    int GrossMinutes,
    int BreakMinutes,
    int NetMinutes,
    bool HasCorrections,
    Guid Version,
    IReadOnlyList<AttendanceBreakDto> Breaks,
    IReadOnlyList<AttendanceCorrectionDto> Corrections);

/// <summary>Eén kalenderdag (tenant-tijdzone) met gewerkte tijd en geplande tijd (Shift-bron).</summary>
public sealed record AttendanceDayDto(
    DateOnly Date,
    int GrossMinutes,
    int BreakMinutes,
    int NetMinutes,
    int? PlannedMinutes,
    IReadOnlyList<AttendanceSessionDto> Sessions)
{
    public int? DeviationMinutes => PlannedMinutes is { } planned ? NetMinutes - planned : null;
}

public sealed record AttendanceHistoryDto(
    DateOnly From,
    DateOnly To,
    int TotalGrossMinutes,
    int TotalBreakMinutes,
    int TotalNetMinutes,
    int? TotalPlannedMinutes,
    IReadOnlyList<AttendanceDayDto> Days);
