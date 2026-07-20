using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Dtos;

/// <summary>Machine-readable conflict codes; the frontend maps them to Dutch labels.</summary>
public enum PlanningConflictCode
{
    DriverAbsent,
    DriverBlocked,
    DriverInactive,
    DriverNotReady,
    DriverDoubleBooked,
    DriverShiftOverlap,
    DriverTraining,
    VehicleNotOperational,
    VehicleInactive,
    VehicleDoubleBooked,
    TrailerNotOperational,
    TrailerInactive,
    TrailerDoubleBooked,
    OrderRequiresCrane,
    OrderRequiresAdr,
    MissingDriver,
    MissingVehicle,
    NoOrders,
    /// <summary>Cargo totals exceed the assigned vehicle/trailer capacity (severity via tenant setting).</summary>
    CapacityExceeded,
    /// <summary>The capacity calculation is incomplete: orders without weight/volume data.</summary>
    CapacityCheckIncomplete,
}

/// <summary>
/// Blocking conflicts stop planning (unless overridden); warnings never do; information is context.
/// <see cref="Blocking"/> mirrors <see cref="Severity"/> for existing consumers.
/// </summary>
public record PlanningConflictDto(
    PlanningConflictCode Code, bool Blocking, string Description, ConflictSeverity Severity)
{
    public PlanningConflictDto(PlanningConflictCode code, bool blocking, string description)
        : this(code, blocking, description, blocking ? ConflictSeverity.Blocking : ConflictSeverity.Warning)
    {
    }
}

public record TripOrderSummaryDto(
    Guid TransportOrderId,
    int Sequence,
    string OrderNumber,
    string CustomerName,
    TransportOrderStatus OrderStatus,
    string GoodsDescription,
    string? FirstLoadingCity,
    string? LastUnloadingCity,
    bool AdrRequired,
    bool CraneRequired);

public record TripListItemDto(
    Guid Id,
    string TripNumber,
    DateOnly TripDate,
    TripStatus Status,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehicleNumber,
    string? VehicleLicensePlate,
    Guid? TrailerId,
    string? TrailerNumber,
    int OrderCount,
    int BlockingConflictCount);

public record TripDetailDto(
    Guid Id,
    string TripNumber,
    DateOnly TripDate,
    TripStatus Status,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehicleNumber,
    string? VehicleLicensePlate,
    Guid? TrailerId,
    string? TrailerNumber,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    decimal? PlannedDistanceKm,
    decimal? PlannedEmptyKm,
    decimal? ActualDistanceKm,
    decimal? ActualEmptyKm,
    string? Notes,
    IReadOnlyList<TripOrderSummaryDto> Orders,
    IReadOnlyList<PlanningConflictDto> Conflicts,
    IReadOnlyList<TripStatus> AllowedTransitions);

public record CreateTripRequest(
    DateOnly TripDate,
    Guid? DriverId,
    Guid? VehicleId,
    Guid? TrailerId,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? Notes,
    IReadOnlyList<Guid> OrderIds,
    decimal? PlannedDistanceKm = null,
    decimal? PlannedEmptyKm = null);

public record UpdateTripRequest(
    DateOnly TripDate,
    Guid? DriverId,
    Guid? VehicleId,
    Guid? TrailerId,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? Notes,
    IReadOnlyList<Guid> OrderIds,
    decimal? PlannedDistanceKm = null,
    decimal? PlannedEmptyKm = null);

public record ChangeTripStatusRequest(
    TripStatus Status, bool Override = false, bool ReleaseOverride = false, string? OverrideReason = null);

public enum TripOperationOutcome
{
    Success,
    NotFound,
    InvalidReference,
    InvalidState,
    ValidationFailed,
    ConflictsBlock,
    PackagesBlock,
}

public record TripOperationResult(
    TripOperationOutcome Outcome,
    TripDetailDto? Trip,
    string? Error = null,
    IReadOnlyList<PlanningConflictDto>? Conflicts = null,
    object? PackageReadiness = null)
{
    public static TripOperationResult Success(TripDetailDto trip) => new(TripOperationOutcome.Success, trip);
    public static readonly TripOperationResult NotFound = new(TripOperationOutcome.NotFound, null);
    public static TripOperationResult InvalidReference(string error) => new(TripOperationOutcome.InvalidReference, null, error);
    public static TripOperationResult InvalidState(string error) => new(TripOperationOutcome.InvalidState, null, error);
    public static TripOperationResult Invalid(string error) => new(TripOperationOutcome.ValidationFailed, null, error);
    public static TripOperationResult Blocked(IReadOnlyList<PlanningConflictDto> conflicts) =>
        new(TripOperationOutcome.ConflictsBlock, null, "De rit kan niet worden gepland door conflicten.", conflicts);
    public static TripOperationResult PackagesBlocked(string error, object readiness) =>
        new(TripOperationOutcome.PackagesBlock, null, error, null, readiness);
}
