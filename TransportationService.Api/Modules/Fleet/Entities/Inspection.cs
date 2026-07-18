using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum InspectionType
{
    VehicleInspection,
    TrailerInspection,
    CraneInspection,
    Other,
}

public enum InspectionResult
{
    Passed,
    PassedWithRemarks,
    Failed,
}

/// <summary>
/// A (periodic) inspection for exactly one vehicle or trailer: statutory vehicle/trailer
/// inspections, crane inspections (typically every 3 months), or tenant-specific kinds.
/// Overdue is computed server-side; completing an inspection that carries an interval plans
/// the next one automatically. The attachment follows the storage-key architecture.
/// </summary>
public class Inspection : AuditableTenantEntity
{
    /// <summary>Exactly one of VehicleId/TrailerId is set (DB check constraint).</summary>
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }

    public InspectionType InspectionType { get; set; }
    public string? CustomTypeName { get; set; }

    public DateOnly DueDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public InspectionResult? Result { get; set; }

    /// <summary>Months between recurring inspections (crane inspections default to 3).</summary>
    public int? IntervalMonths { get; set; }

    /// <summary>Days before the due date at which the inspection counts as due soon.</summary>
    public int? WarningDays { get; set; }

    /// <summary>Opaque IFileStorageService storage key of the report/certificate, when present.</summary>
    public string? AttachmentPath { get; set; }

    public string? Notes { get; set; }
}
