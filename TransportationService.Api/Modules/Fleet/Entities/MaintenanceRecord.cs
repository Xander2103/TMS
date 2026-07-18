using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum MaintenanceType
{
    PeriodicService,
    Repair,
    TireService,
    BrakeService,
    Revision,
    Other,
}

public enum MaintenanceStatus
{
    Planned,
    InProgress,
    Completed,
    Cancelled,
}

/// <summary>
/// A maintenance job for exactly one vehicle or trailer. Overdue is computed server-side
/// (Planned/InProgress past its date or odometer trigger). Completing a job that carries an
/// interval (months and/or km) automatically plans the follow-up job — that is the recurring-
/// maintenance mechanism. The attachment follows the storage-key architecture.
/// </summary>
public class MaintenanceRecord : AuditableTenantEntity
{
    /// <summary>Exactly one of VehicleId/TrailerId is set (DB check constraint).</summary>
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }

    public MaintenanceType MaintenanceType { get; set; }
    public string? CustomTypeName { get; set; }
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Planned;

    public string Description { get; set; } = string.Empty;

    // Triggers
    public DateOnly? ScheduledDate { get; set; }
    /// <summary>Job becomes due when the vehicle's odometer reaches this value (vehicles only).</summary>
    public int? OdometerTriggerKm { get; set; }

    // Completion
    public DateOnly? CompletedDate { get; set; }
    public int? CompletedOdometerKm { get; set; }
    public string? WorkPerformed { get; set; }
    public string? Provider { get; set; }
    public decimal? Cost { get; set; }

    // Follow-up planning
    public DateOnly? NextServiceDate { get; set; }
    public int? NextServiceOdometerKm { get; set; }

    // Recurrence (applied when the job is completed)
    public int? IntervalMonths { get; set; }
    public int? IntervalKm { get; set; }

    /// <summary>Opaque IFileStorageService storage key of the attachment, when present.</summary>
    public string? AttachmentPath { get; set; }

    public string? Notes { get; set; }
}
