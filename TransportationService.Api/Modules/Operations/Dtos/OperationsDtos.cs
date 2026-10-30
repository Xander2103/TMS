using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Operations.Dtos;

/// <summary>
/// Where a position estimate comes from. LiveGps/LastKnownGps are reserved for a future
/// telematics integration — the platform NEVER fabricates them; today positions derive from
/// scan events, completed stops or the planned route, or are honestly Unavailable.
/// </summary>
public enum LocationSource
{
    LiveGps,
    LastKnownGps,
    ScanLocation,
    StopLocation,
    PlannedLocation,
    Unavailable,
}

public record TripPositionDto(
    LocationSource Source,
    decimal? Latitude,
    decimal? Longitude,
    DateTime? Timestamp,
    string? Description);

public record OperationsStopDto(
    Guid TransportOrderStopId,
    string? City,
    string? LocationName,
    StopExecutionStatus Status,
    DateTime? PlannedFrom,
    DateTime? PlannedTo,
    DateTime? CurrentEta,
    EtaSource? EtaSource,
    EtaStatus? EtaStatus);

public record OperationsTripDto(
    Guid Id,
    string TripNumber,
    DateOnly TripDate,
    TripStatus Status,
    string? DriverName,
    string? VehicleNumber,
    string? TrailerNumber,
    int StopCount,
    int CompletedStopCount,
    OperationsStopDto? CurrentStop,
    OperationsStopDto? NextStop,
    /// <summary>Worst live ETA status over the pending stops (null = no ETA data).</summary>
    EtaStatus? EtaStatus,
    EtaSource? EtaSource,
    int? DelayMinutes,
    TripPositionDto Position,
    DateTime? LastScanAt,
    string? LastScanResult,
    int OpenExceptionCount,
    int MissingPodCount,
    /// <summary>Orders on the trip in sequence (B21): the dispatcher sees WHAT is being driven, not only who drives.</summary>
    IReadOnlyList<OperationsTripOrderDto> Orders);

public record OperationsTripOrderDto(
    Guid Id,
    string OrderNumber,
    string? CustomerName,
    Guid? DossierId,
    string? DossierNumber);

public record OperationsCountersDto(
    int ActiveTrips,
    int DelayedTrips,
    int OpenExceptions,
    int OpenCriticalIncidents,
    int MissingPods,
    int ActiveAlerts,
    int CriticalAlerts,
    /// <summary>UX-008: failed drops whose incident is still open, also when the trip has completed.</summary>
    int FailedDeliveries = 0);

public record OperationsOverviewDto(
    DateTime GeneratedAt,
    OperationsCountersDto Counters,
    IReadOnlyList<OperationsTripDto> Trips,
    IReadOnlyList<OperationsFailedDeliveryDto>? FailedDeliveries = null);

/// <summary>
/// UX-008: a failed drop as the dispatcher's work item. A failed drop finishes the stop and often the trip, so
/// it would leave the trip table; it stays here until the incident the failure already created is handled.
/// </summary>
public record OperationsFailedDeliveryDto(
    Guid IncidentId,
    Guid? OrderId,
    string? OrderNumber,
    Guid? TripId,
    string? TripNumber,
    string? CustomerName,
    Guid? DossierId,
    string? DossierNumber,
    string Description,
    DateTime OccurredAt,
    Guid? RedeliveryOrderId,
    string? RedeliveryOrderNumber);
