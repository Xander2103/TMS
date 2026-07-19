using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Dtos;

/// <summary>One executable stop in trip order: order sequence first, stop sequence within the order.</summary>
public record ExecutionStopDto(
    Guid TransportOrderStopId,
    Guid TransportOrderId,
    string OrderNumber,
    string CustomerName,
    int OrderSequence,
    int StopSequence,
    StopType StopType,
    string LocationName,
    string? Address,
    string? PostalCode,
    string? City,
    DateTime? PlannedFrom,
    DateTime? PlannedTo,
    DateTime? RequestedFrom,
    DateTime? RequestedTo,
    DateTime? ConfirmedFrom,
    DateTime? ConfirmedTo,
    DateTime? EarliestAllowed,
    DateTime? LatestAllowed,
    bool AppointmentRequired,
    string? AppointmentReference,
    string? Instructions,
    string? AccessInstructions,
    string? LoadingInstructions,
    string? UnloadingInstructions,
    StopExecutionStatus Status,
    DateTime? ArrivedAt,
    DateTime? DepartedAt,
    DateTime? CompletedAt,
    /// <summary>Minutes between arrival and the start of handling (or departure when handling was never logged).</summary>
    int? WaitingMinutes,
    string? LateArrivalReason,
    string? StatusReason,
    IReadOnlyList<StopExecutionStatus> AllowedTransitions,
    bool HasPod,
    string? PodSignedBy,
    string? Remarks);

public record TripExecutionDto(
    Guid TripId,
    string TripNumber,
    DateOnly TripDate,
    TripStatus TripStatus,
    string? DriverName,
    string? VehicleNumber,
    string? VehicleLicensePlate,
    IReadOnlyList<ExecutionStopDto> Stops,
    int CompletedCount,
    int TotalCount);

/// <summary>A trip as the assigned driver sees it in "Mijn ritten".</summary>
public record MyTripDto(
    Guid Id,
    string TripNumber,
    DateOnly TripDate,
    TripStatus Status,
    string? VehicleNumber,
    string? VehicleLicensePlate,
    string? TrailerNumber,
    int OrderCount,
    int StopCount,
    int CompletedStopCount);

/// <summary>
/// Generic controlled status transition. The reason is mandatory for Skipped, Failed and
/// PartiallyCompleted, and for an arrival past the latest allowed bound. Notes update the
/// free-form stop remarks.
/// </summary>
public record TransitionStopRequest(StopExecutionStatus ToStatus, string? Reason = null, string? Notes = null);

public record CompleteStopRequest(string? PodSignedBy, string? Remarks, string? Reason = null);

public record SkipStopRequest(string Remarks);

public record StopStatusHistoryDto(
    StopExecutionStatus FromStatus,
    StopExecutionStatus ToStatus,
    DateTime OccurredAt,
    string? UserName,
    string? Reason);

public enum ExecutionOutcome
{
    Success,
    NotFound,
    NotYourTrip,
    InvalidState,
    ValidationFailed,
}

public record ExecutionResult(ExecutionOutcome Outcome, TripExecutionDto? Execution, string? Error = null)
{
    public static ExecutionResult Success(TripExecutionDto execution) => new(ExecutionOutcome.Success, execution);
    public static readonly ExecutionResult NotFound = new(ExecutionOutcome.NotFound, null);
    public static readonly ExecutionResult NotYourTrip = new(ExecutionOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
    public static ExecutionResult InvalidState(string error) => new(ExecutionOutcome.InvalidState, null, error);
    public static ExecutionResult Invalid(string error) => new(ExecutionOutcome.ValidationFailed, null, error);
}

public record StopHistoryResult(ExecutionOutcome Outcome, IReadOnlyList<StopStatusHistoryDto>? History, string? Error = null)
{
    public static StopHistoryResult Success(IReadOnlyList<StopStatusHistoryDto> history) => new(ExecutionOutcome.Success, history);
    public static readonly StopHistoryResult NotFound = new(ExecutionOutcome.NotFound, null);
    public static readonly StopHistoryResult NotYourTrip = new(ExecutionOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
}
