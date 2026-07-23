using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Dtos;

public record AbsenceDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    bool IsDriver,
    AbsenceType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    AbsenceStatus Status,
    string? Reason,
    string? DecisionNote,
    DateTime? DecidedAt,
    AbsencePartDay PartDay = AbsencePartDay.FullDay,
    /// <summary>HR-only; the portal blanks this before it reaches the employee.</summary>
    string? InternalNote = null,
    bool HasAttachment = false,
    string? AttachmentFileName = null,
    /// <summary>Configurable leave type (source of truth for balance deduction); null on legacy rows.</summary>
    Guid? LeaveTypeId = null);

public record CreateAbsenceRequest(
    AbsenceType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason,
    AbsencePartDay PartDay = AbsencePartDay.FullDay,
    Guid? LeaveTypeId = null);

public record UpdateAbsenceRequest(
    AbsenceType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason,
    AbsencePartDay PartDay = AbsencePartDay.FullDay,
    Guid? LeaveTypeId = null);

public record DecideAbsenceRequest(bool Approve, string? Note);

/// <summary>HR asks the employee to adjust the request; optionally proposing alternative dates.</summary>
public record RequestAbsenceChangesRequest(string Note, DateOnly? ProposedStartDate, DateOnly? ProposedEndDate);

public record SetInternalNoteRequest(string? Note);

public record OverlappingShiftDto(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string? WorkLocation);

public record OverlappingTripDto(Guid TripId, string TripNumber, DateOnly TripDate);

public record OverlappingColleagueDto(
    string EmployeeName, AbsenceType Type, DateOnly StartDate, DateOnly EndDate, AbsenceStatus Status);

/// <summary>
/// Everything HR sees next to a request: planning conflicts, colleagues off in the same
/// period (same department) and the balance architecture (used vacation days; entitlement
/// administration is a future extension).
/// </summary>
public record AbsenceReviewContextDto(
    IReadOnlyList<OverlappingShiftDto> OverlappingShifts,
    IReadOnlyList<OverlappingTripDto> OverlappingTrips,
    IReadOnlyList<OverlappingColleagueDto> OverlappingColleagues,
    int UsedVacationDaysThisYear,
    bool HasAttachment);

public enum AbsenceOperationOutcome
{
    Success,
    NotFound,
    OwnerNotFound,
    Overlap,
    InvalidState,
    ValidationFailed,
}

public record AbsenceOperationResult(AbsenceOperationOutcome Outcome, AbsenceDto? Absence, string? Error = null)
{
    public static AbsenceOperationResult Success(AbsenceDto absence) => new(AbsenceOperationOutcome.Success, absence);
    public static readonly AbsenceOperationResult NotFound = new(AbsenceOperationOutcome.NotFound, null);
    public static readonly AbsenceOperationResult OwnerNotFound = new(AbsenceOperationOutcome.OwnerNotFound, null);
    public static readonly AbsenceOperationResult Overlap = new(AbsenceOperationOutcome.Overlap, null);
    public static AbsenceOperationResult InvalidState(string error) => new(AbsenceOperationOutcome.InvalidState, null, error);
    public static AbsenceOperationResult Invalid(string error) => new(AbsenceOperationOutcome.ValidationFailed, null, error);
}
