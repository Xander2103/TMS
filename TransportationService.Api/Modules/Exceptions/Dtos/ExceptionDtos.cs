using TransportationService.Api.Modules.Exceptions.Entities;

namespace TransportationService.Api.Modules.Exceptions.Dtos;

public record ReportExceptionRequest(
    ExecutionExceptionType Type,
    ExceptionSeverity Severity,
    string Description,
    Guid? TransportOrderStopId = null,
    Guid? CargoItemId = null,
    decimal? Quantity = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

/// <summary>Back-office fields; the reporter's description and context never change afterwards.</summary>
public record UpdateExceptionRequest(ExceptionSeverity Severity, string? DispatcherNotes, bool CustomerVisible);

/// <summary>Terminal statuses (Resolved/Rejected) demand a note; it becomes the resolution note.</summary>
public record ChangeExceptionStatusRequest(ExecutionExceptionStatus Status, string? Note);

public record ExceptionPhotoDto(Guid Id, string FileName, string ContentType, DateTime CreatedAt);

public record ExceptionListItemDto(
    Guid Id,
    DateTime OccurredAt,
    ExecutionExceptionType Type,
    ExceptionSeverity Severity,
    ExecutionExceptionStatus Status,
    string Description,
    string TripNumber,
    string? OrderNumber,
    string? StopLabel,
    string? ReportedByName,
    int PhotoCount,
    bool CustomerVisible,
    Guid? PackageId = null,
    string? PackageNumber = null);

public record ExceptionDetailDto(
    Guid Id,
    ExecutionExceptionType Type,
    ExceptionSeverity Severity,
    ExecutionExceptionStatus Status,
    string Description,
    decimal? Quantity,
    Guid TripId,
    string TripNumber,
    Guid? TransportOrderId,
    string? OrderNumber,
    Guid? TransportOrderStopId,
    string? StopLabel,
    Guid? CargoItemId,
    string? CargoDescription,
    Guid? PackageId,
    string? PackageNumber,
    string? PackageStatus,
    string? ReportedByName,
    string? DriverName,
    DateTime OccurredAt,
    decimal? Latitude,
    decimal? Longitude,
    string? DispatcherNotes,
    bool CustomerVisible,
    string? ResolutionNote,
    string? ResolvedByName,
    DateTime? ResolvedAt,
    IReadOnlyList<ExceptionPhotoDto> Photos,
    IReadOnlyList<ExecutionExceptionStatus> AllowedTransitions);

public enum ExceptionOutcome
{
    Success,
    NotFound,
    NotYourTrip,
    InvalidState,
    ValidationFailed,
}

public record ExceptionOperationResult(ExceptionOutcome Outcome, ExceptionDetailDto? Exception, string? Error = null)
{
    public static ExceptionOperationResult Success(ExceptionDetailDto exception) => new(ExceptionOutcome.Success, exception);
    public static readonly ExceptionOperationResult NotFound = new(ExceptionOutcome.NotFound, null);
    public static readonly ExceptionOperationResult NotYourTrip = new(ExceptionOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
    public static ExceptionOperationResult InvalidState(string error) => new(ExceptionOutcome.InvalidState, null, error);
    public static ExceptionOperationResult Invalid(string error) => new(ExceptionOutcome.ValidationFailed, null, error);
}

public record ExceptionListResult(ExceptionOutcome Outcome, IReadOnlyList<ExceptionListItemDto>? Exceptions, string? Error = null)
{
    public static ExceptionListResult Success(IReadOnlyList<ExceptionListItemDto> exceptions) => new(ExceptionOutcome.Success, exceptions);
    public static readonly ExceptionListResult NotFound = new(ExceptionOutcome.NotFound, null);
    public static readonly ExceptionListResult NotYourTrip = new(ExceptionOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
}
