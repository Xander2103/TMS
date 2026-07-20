using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Dtos;

// ---------------------------------------------------------------------------
// Board (center zone): trips as blocks in a bounded date range.
// ---------------------------------------------------------------------------

public record PlanningBoardTripDto(
    Guid Id,
    string TripNumber,
    DateOnly TripDate,
    TripStatus Status,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehicleNumber,
    Guid? TrailerId,
    string? TrailerNumber,
    int OrderCount,
    int StopCount,
    string? RouteSummary,
    decimal? TotalWeightKg,
    decimal? TotalVolumeM3,
    decimal? CapacityWeightKg,
    decimal? CapacityVolumeM3,
    int BlockingConflictCount,
    int WarningConflictCount,
    Guid Version);

public record PlanningBoardDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PlanningBoardTripDto> Trips);

// ---------------------------------------------------------------------------
// Unplanned work (left zone): orders awaiting planning, paged and filtered.
// ---------------------------------------------------------------------------

public record UnplannedOrderDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    Guid CustomerId,
    string CustomerName,
    TransportOrderStatus Status,
    OrderPriority Priority,
    string? FirstLoadingCity,
    string? LastUnloadingCity,
    DateTime? RequestedFrom,
    DateTime? RequestedTo,
    string? GoodsDescription,
    int StopCount,
    int CargoLineCount,
    int PackageCount,
    decimal? TotalWeightKg,
    decimal? TotalVolumeM3,
    bool AdrRequired,
    bool CraneRequired,
    /// <summary>Machine badges: MissingWeight, MissingVolume, AppointmentRequired, NoStops, SubmittedReview.</summary>
    IReadOnlyList<string> AttentionBadges);

public record UnplannedOrdersQuery(
    string? Search = null,
    Guid? CustomerId = null,
    TransportOrderStatus? Status = null,
    OrderPriority? Priority = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    bool OnlyWithAttention = false,
    int Page = 1,
    int PageSize = 50);

// ---------------------------------------------------------------------------
// Resources (right zone): drivers / vehicles / trailers with availability
// and warning context for the requested range.
// ---------------------------------------------------------------------------

public record ResourceAssignmentDto(DateOnly Date, Guid TripId, string TripNumber, TripStatus Status);

public record ResourceAbsenceDto(DateOnly StartDate, DateOnly EndDate, AbsenceType Type);

public record PlanningDriverDto(
    Guid Id,
    string DriverNumber,
    string Name,
    DriverAvailabilityStatus AvailabilityStatus,
    bool IsActive,
    bool IsBlocked,
    string? BlockReason,
    Guid? FixedVehicleId,
    string? FixedVehicleNumber,
    IReadOnlyList<ResourceAssignmentDto> Assignments,
    IReadOnlyList<ResourceAbsenceDto> Absences,
    /// <summary>Qualification names that are expired/suspended/rejected (hard problems).</summary>
    IReadOnlyList<string> QualificationBlocks,
    /// <summary>Qualification names expiring within the tenant's warning window.</summary>
    IReadOnlyList<string> QualificationWarnings);

public record PlanningVehicleDto(
    Guid Id,
    string InternalNumber,
    string LicensePlate,
    VehicleOperationalStatus OperationalStatus,
    bool IsActive,
    decimal? PayloadKg,
    decimal? VolumeM3,
    bool HasCrane,
    bool HasTailLift,
    bool HasRefrigeration,
    bool AdrSuitable,
    Guid? FixedDriverId,
    string? FixedDriverName,
    Guid? CurrentDriverId,
    string? CurrentDriverName,
    int OverdueMaintenanceCount,
    int OverdueInspectionCount,
    IReadOnlyList<ResourceAssignmentDto> Assignments);

public record PlanningTrailerDto(
    Guid Id,
    string InternalNumber,
    string LicensePlate,
    TrailerOperationalStatus OperationalStatus,
    bool IsActive,
    decimal? CapacityKg,
    decimal? VolumeM3,
    bool HasRefrigeration,
    bool AdrSuitable,
    int OverdueMaintenanceCount,
    int OverdueInspectionCount,
    IReadOnlyList<ResourceAssignmentDto> Assignments);

public record PlanningResourcesDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PlanningDriverDto> Drivers,
    IReadOnlyList<PlanningVehicleDto> Vehicles,
    IReadOnlyList<PlanningTrailerDto> Trailers);

// ---------------------------------------------------------------------------
// Targeted trip commands (drag-and-drop mutations).
// ---------------------------------------------------------------------------

public record AssignOrdersRequest(IReadOnlyList<Guid> OrderIds, Guid? Version = null);

public record ReorderTripOrdersRequest(IReadOnlyList<Guid> OrderIds, Guid? Version = null);

/// <summary>Null id clears the slot. Override applies when the trip is Planned and blocking conflicts arise.</summary>
public record AssignResourceRequest(Guid? ResourceId, Guid? Version = null, bool Override = false, string? OverrideReason = null);

public record RescheduleTripRequest(
    DateOnly TripDate, DateTime? PlannedStart, DateTime? PlannedEnd,
    Guid? Version = null, bool Override = false, string? OverrideReason = null);

/// <summary>Dry-run: evaluate the conflicts a hypothetical assignment WOULD create; never mutates.</summary>
public record ValidateAssignmentRequest(
    Guid? DriverId = null,
    Guid? VehicleId = null,
    Guid? TrailerId = null,
    DateOnly? TripDate = null,
    DateTime? PlannedStart = null,
    DateTime? PlannedEnd = null,
    IReadOnlyList<Guid>? AdditionalOrderIds = null);

public record ValidateAssignmentResultDto(IReadOnlyList<PlanningConflictDto> Conflicts, bool HasBlocking);
