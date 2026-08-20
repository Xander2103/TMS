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

// ── Kiosk (prikklok) ─────────────────────────────────────────────────────────────

public sealed record KioskDeviceDto(
    Guid Id,
    string Name,
    Guid? LocationId,
    string? LocationName,
    bool IsActive,
    DateTime? LastSeenAt,
    DateTime? LastPunchAt,
    DateTime CreatedAt);

/// <summary>Provisioningresultaat: de deviceKey wordt hier éénmalig teruggegeven en is daarna onherleidbaar.</summary>
public sealed record KioskProvisionResult(KioskDeviceDto Device, string DeviceKey);

public sealed record SaveKioskDeviceRequest(string Name, Guid? LocationId, bool IsActive = true);

public enum KioskOutcome
{
    Success,
    InvalidDevice,
    InvalidCode,
    KioskDisabled,
    NotConfigured,
    TokenExpired,
    InvalidAction,
    PunchRejected,
}

/// <summary>Minimale identificatierespons: voornaam + status + kortlevend interactietoken. Nooit meer persoonsgegevens dan dit.</summary>
public sealed record KioskIdentifyResult(
    KioskOutcome Outcome,
    string? FirstName,
    AttendanceStatusDto? Status,
    string? InteractionToken,
    string? Error);

public sealed record KioskPunchResult(
    KioskOutcome Outcome,
    AttendanceStatusDto? Status,
    DateTime? OccurredAt,
    string? Error);

public sealed record KioskPingResult(
    KioskOutcome Outcome,
    string? DeviceName,
    string? LocationName,
    string? Error);

public enum KioskPunchAction
{
    ClockIn,
    ClockOut,
    StartBreak,
    EndBreak,
}

// ── Credentials (PIN-beheer) ─────────────────────────────────────────────────────

public sealed record AttendanceCredentialStatusDto(
    bool HasCredential,
    bool IsActive,
    DateTime? LastUsedAt,
    DateTime? LockedUntil);

/// <summary>Resultaat van PIN genereren/zetten: GeneratedPin is alleen gevuld bij genereren en wordt nooit opgeslagen of gelogd.</summary>
public sealed record AttendanceCredentialResult(
    AttendanceCredentialOutcome Outcome,
    AttendanceCredentialStatusDto? Status,
    string? GeneratedPin,
    string? Error);

public enum AttendanceCredentialOutcome
{
    Success,
    EmployeeNotFound,
    InvalidPin,
    PinInUse,
    NotConfigured,
    NoCredential,
}

public sealed record SetAttendancePinRequest(string? Pin);
