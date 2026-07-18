using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public record MaintenanceRecordDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    MaintenanceType MaintenanceType,
    string? CustomTypeName,
    MaintenanceStatus Status,
    bool IsOverdue,
    string Description,
    DateOnly? ScheduledDate,
    int? OdometerTriggerKm,
    DateOnly? CompletedDate,
    int? CompletedOdometerKm,
    string? WorkPerformed,
    string? Provider,
    decimal? Cost,
    DateOnly? NextServiceDate,
    int? NextServiceOdometerKm,
    int? IntervalMonths,
    int? IntervalKm,
    bool HasAttachment,
    string? Notes);

/// <summary>Row for the maintenance-due overview (fleet dashboard / warnings).</summary>
public record DueMaintenanceDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    string OwnerNumber,
    string OwnerLicensePlate,
    MaintenanceType MaintenanceType,
    string? CustomTypeName,
    string Description,
    MaintenanceStatus Status,
    DateOnly? ScheduledDate,
    int? OdometerTriggerKm,
    bool IsOverdue);

public record CreateMaintenanceRequest(
    MaintenanceType MaintenanceType,
    string? CustomTypeName,
    string Description,
    DateOnly? ScheduledDate,
    int? OdometerTriggerKm,
    string? Provider,
    int? IntervalMonths,
    int? IntervalKm,
    string? Notes);

public record UpdateMaintenanceRequest(
    MaintenanceType MaintenanceType,
    string? CustomTypeName,
    MaintenanceStatus Status,
    string Description,
    DateOnly? ScheduledDate,
    int? OdometerTriggerKm,
    string? Provider,
    int? IntervalMonths,
    int? IntervalKm,
    string? Notes);

public record CompleteMaintenanceRequest(
    DateOnly CompletedDate,
    int? CompletedOdometerKm,
    string? WorkPerformed,
    string? Provider,
    decimal? Cost,
    string? Notes);

public enum MaintenanceOperationOutcome
{
    Success,
    NotFound,
    OwnerNotFound,
    ValidationFailed,
    AlreadyCompleted,
}

public record MaintenanceOperationResult(
    MaintenanceOperationOutcome Outcome,
    MaintenanceRecordDto? Record,
    MaintenanceRecordDto? FollowUp = null,
    string? Error = null)
{
    public static MaintenanceOperationResult Success(MaintenanceRecordDto record, MaintenanceRecordDto? followUp = null) =>
        new(MaintenanceOperationOutcome.Success, record, followUp);
    public static readonly MaintenanceOperationResult NotFound = new(MaintenanceOperationOutcome.NotFound, null);
    public static readonly MaintenanceOperationResult OwnerNotFound = new(MaintenanceOperationOutcome.OwnerNotFound, null);
    public static readonly MaintenanceOperationResult AlreadyCompleted = new(MaintenanceOperationOutcome.AlreadyCompleted, null);
    public static MaintenanceOperationResult Invalid(string error) => new(MaintenanceOperationOutcome.ValidationFailed, null, null, error);
}
