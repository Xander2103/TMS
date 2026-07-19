using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.EmployeePlanning.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Dtos;

public record CreateShiftRequest(
    Guid EmployeeId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    ShiftType Type,
    string? WorkLocation,
    string? RoleLabel,
    string? Notes,
    bool Override = false);

public record UpdateShiftRequest(
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    ShiftType Type,
    string? WorkLocation,
    string? RoleLabel,
    string? Notes,
    bool Override = false);

/// <summary>Copies a whole week of shifts (as Draft) to a target week; collisions are skipped, never overwritten.</summary>
public record CopyWeekRequest(
    DateOnly SourceWeekStart,
    DateOnly TargetWeekStart,
    Guid? EmployeeId,
    Guid? DepartmentId);

public record ShiftDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    int PlannedMinutes,
    ShiftType Type,
    ShiftStatus Status,
    string? WorkLocation,
    string? RoleLabel,
    string? Notes);

/// <summary>
/// One explainable schedule cell state. Shift statuses and absence outcomes share the same
/// vocabulary so the grid legend covers everything an employee's day can be.
/// </summary>
public enum ScheduleEntryState
{
    Draft,
    Planned,
    Confirmed,
    Standby,
    Training,
    LeaveRequested,
    LeaveApproved,
    LeaveRejected,
    Sick,
    Unavailable,
    Trip,
    TripCancelled,
}

public record ScheduleEntryDto(
    ScheduleEntryState State,
    Guid? ShiftId,
    Guid? AbsenceId,
    Guid? TripId,
    string SourceType,
    string Label,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    ShiftType? ShiftType,
    string? WorkLocation,
    string? VehicleSummary,
    string? StatusLabel,
    ConflictSeverity? ConflictSeverity = null,
    IReadOnlyList<string>? ConflictNotes = null);

public record ScheduleDayDto(DateOnly Date, IReadOnlyList<ScheduleEntryDto> Entries);

public record ScheduleRowDto(
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    string? DepartmentName,
    int PlannedMinutes,
    IReadOnlyList<ScheduleDayDto> Days);

public record ScheduleGridDto(DateOnly From, DateOnly To, IReadOnlyList<ScheduleRowDto> Rows);

public enum ShiftOutcome
{
    Success,
    NotFound,
    InvalidState,
    ValidationFailed,
    Overlap,
    Conflict,
}

public record ShiftOperationResult(
    ShiftOutcome Outcome, ShiftDto? Shift, string? Error = null, int CopiedCount = 0, int SkippedCount = 0,
    IReadOnlyList<string>? Conflicts = null)
{
    public static ShiftOperationResult Success(ShiftDto shift) => new(ShiftOutcome.Success, shift);
    public static ShiftOperationResult Copied(int copied, int skipped) => new(ShiftOutcome.Success, null, null, copied, skipped);
    public static readonly ShiftOperationResult NotFound = new(ShiftOutcome.NotFound, null);
    public static ShiftOperationResult InvalidState(string error) => new(ShiftOutcome.InvalidState, null, error);
    public static ShiftOperationResult Invalid(string error) => new(ShiftOutcome.ValidationFailed, null, error);
    public static ShiftOperationResult OverlapError(string error) => new(ShiftOutcome.Overlap, null, error);
    public static ShiftOperationResult ConflictBlocked(IReadOnlyList<string> conflicts) =>
        new(ShiftOutcome.Conflict, null, "De shift botst met bestaande planning.", 0, 0, conflicts);
}
