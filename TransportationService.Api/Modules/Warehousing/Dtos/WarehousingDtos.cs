using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Warehousing.Entities;

namespace TransportationService.Api.Modules.Warehousing.Dtos;

// --- Master data ---

public record DockDto(
    Guid Id, string Code, string? Name,
    bool AllowsLoading, bool AllowsUnloading, bool AllowsAdr, bool Refrigerated,
    decimal? MaxVehicleLengthM, decimal? MaxVehicleHeightM, bool IsActive, string? Notes);

public record WarehouseDto(
    Guid Id, string Name, Guid LocationId, string LocationLabel, bool IsActive,
    TimeOnly? OpensAt, TimeOnly? ClosesAt,
    string? ContactName, string? ContactPhone, string? ContactEmail, string? Notes,
    IReadOnlyList<DockDto> Docks);

public record SaveWarehouseRequest(
    string Name, Guid LocationId, bool IsActive,
    TimeOnly? OpensAt, TimeOnly? ClosesAt,
    string? ContactName, string? ContactPhone, string? ContactEmail, string? Notes);

public record SaveDockRequest(
    string Code, string? Name,
    bool AllowsLoading, bool AllowsUnloading, bool AllowsAdr, bool Refrigerated,
    decimal? MaxVehicleLengthM, decimal? MaxVehicleHeightM, bool IsActive, string? Notes);

// --- Appointments ---

public record DockConflictDto(
    string Code,
    ConflictSeverity Severity,
    string Description,
    bool OverrideAllowed);

public record DockAppointmentDto(
    Guid Id,
    Guid WarehouseId,
    Guid? DockId,
    string? DockCode,
    DockOperationType OperationType,
    DockAppointmentStatus Status,
    DateTime PlannedStart,
    DateTime PlannedEnd,
    DateTime? ArrivedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    OrderPriority Priority,
    Guid? TripId,
    string? TripNumber,
    Guid? TransportOrderId,
    string? OrderNumber,
    string? CustomerName,
    Guid? VehicleId,
    string? VehicleNumber,
    Guid? TrailerId,
    string? TrailerNumber,
    Guid? DriverId,
    string? DriverName,
    string? Reference,
    string? Remarks,
    int PackageCount,
    int PackagesHandled,
    bool HasOpenDiscrepancies,
    IReadOnlyList<DockAppointmentStatus> AllowedTransitions,
    Guid Version);

public record SaveDockAppointmentRequest(
    Guid WarehouseId,
    Guid? DockId,
    DockOperationType OperationType,
    DateTime PlannedStart,
    DateTime PlannedEnd,
    Guid? TripId = null,
    Guid? TransportOrderId = null,
    Guid? VehicleId = null,
    Guid? TrailerId = null,
    Guid? DriverId = null,
    OrderPriority Priority = OrderPriority.Normal,
    string? Reference = null,
    string? Remarks = null,
    Guid? Version = null,
    bool Override = false,
    string? OverrideReason = null);

public record ChangeDockAppointmentStatusRequest(
    DockAppointmentStatus Status, Guid? Version = null);

public record DockBoardDto(
    Guid WarehouseId,
    DateOnly Date,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    IReadOnlyList<DockDto> Docks,
    IReadOnlyList<DockAppointmentDto> Appointments,
    /// <summary>Arrived/Waiting appointments without a dock, priority-then-arrival ordered.</summary>
    IReadOnlyList<DockAppointmentDto> Queue);

public record DockUtilizationDto(Guid DockId, string DockCode, int BookedMinutes, int OpenMinutes, decimal UtilizationPct);

public record WarehouseDashboardDto(
    Guid WarehouseId,
    DateOnly Date,
    int ExpectedToday,
    int Waiting,
    int InProgress,
    int Completed,
    int Delayed,
    int NoShows,
    IReadOnlyList<DockUtilizationDto> Utilization);

public enum DockOperationOutcome
{
    Success,
    NotFound,
    InvalidReference,
    InvalidState,
    ValidationFailed,
    ConflictsBlock,
    StaleVersion,
}

public record DockOperationResult(
    DockOperationOutcome Outcome,
    DockAppointmentDto? Appointment,
    string? Error = null,
    IReadOnlyList<DockConflictDto>? Conflicts = null)
{
    public static DockOperationResult Success(DockAppointmentDto appointment) => new(DockOperationOutcome.Success, appointment);
    public static readonly DockOperationResult NotFound = new(DockOperationOutcome.NotFound, null);
    public static DockOperationResult InvalidReference(string error) => new(DockOperationOutcome.InvalidReference, null, error);
    public static DockOperationResult InvalidState(string error) => new(DockOperationOutcome.InvalidState, null, error);
    public static DockOperationResult Invalid(string error) => new(DockOperationOutcome.ValidationFailed, null, error);
    public static DockOperationResult Blocked(IReadOnlyList<DockConflictDto> conflicts) =>
        new(DockOperationOutcome.ConflictsBlock, null, "De afspraak botst met de dockplanning.", conflicts);
    public static DockOperationResult Stale(DockAppointmentDto current) =>
        new(DockOperationOutcome.StaleVersion, current,
            "De afspraak is intussen door iemand anders gewijzigd. Controleer de actuele gegevens en probeer opnieuw.");
}
