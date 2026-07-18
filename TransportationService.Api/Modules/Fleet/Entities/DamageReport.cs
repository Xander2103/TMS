using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum DamageSeverity
{
    Minor,
    Moderate,
    Severe,
    TotalLoss,
}

public enum DamageStatus
{
    Reported,
    UnderAssessment,
    InRepair,
    Repaired,
    Closed,
}

/// <summary>
/// A damage/incident report for exactly one vehicle or trailer, optionally linked to the
/// driver involved. Photos/attachments follow the storage-key architecture.
/// </summary>
public class DamageReport : AuditableTenantEntity
{
    /// <summary>Exactly one of VehicleId/TrailerId is set (DB check constraint).</summary>
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }

    /// <summary>The driver involved in the incident, when known.</summary>
    public Guid? DriverId { get; set; }

    public DateOnly IncidentDate { get; set; }
    public string? Location { get; set; }
    public string Description { get; set; } = string.Empty;
    public DamageSeverity Severity { get; set; } = DamageSeverity.Minor;
    public DamageStatus Status { get; set; } = DamageStatus.Reported;

    public string? InsuranceReference { get; set; }
    public decimal? RepairCost { get; set; }
    public int? DowntimeDays { get; set; }

    /// <summary>Opaque IFileStorageService storage key for photos/documents, when present.</summary>
    public string? AttachmentPath { get; set; }

    public string? Notes { get; set; }
}
