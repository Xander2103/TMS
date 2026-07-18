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
    DateTime? DecidedAt);

public record CreateAbsenceRequest(
    AbsenceType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);

public record UpdateAbsenceRequest(
    AbsenceType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);

public record DecideAbsenceRequest(bool Approve, string? Note);

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
