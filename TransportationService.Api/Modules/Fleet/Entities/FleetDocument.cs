using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum FleetDocumentType
{
    Registration,
    Insurance,
    TechnicalInspection,
    Conformity,
    CraneInspection,
    RefrigerationCertificate,
    AdrCertificate,
    Other,
}

/// <summary>
/// A document belonging to exactly one vehicle or one trailer (registration, insurance,
/// inspection certificate, …). Expiry status is computed server-side from ExpiryDate and the
/// warning window. The attachment follows the established storage-key architecture
/// (<c>DocumentPath</c> holds an opaque IFileStorageService key; upload endpoints are a later
/// wave, matching the qualifications module).
/// </summary>
public class FleetDocument : AuditableTenantEntity
{
    /// <summary>Set when the document belongs to a vehicle. Exactly one of VehicleId/TrailerId is set (DB check constraint).</summary>
    public Guid? VehicleId { get; set; }

    /// <summary>Set when the document belongs to a trailer.</summary>
    public Guid? TrailerId { get; set; }

    public FleetDocumentType DocumentType { get; set; }

    /// <summary>Display name for tenant-specific document kinds; required when DocumentType is Other.</summary>
    public string? CustomTypeName { get; set; }

    public string? DocumentNumber { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Days before expiry at which the document counts as expiring; falls back to the module default.</summary>
    public int? WarningDays { get; set; }

    /// <summary>Opaque IFileStorageService storage key of the attachment, when present.</summary>
    public string? DocumentPath { get; set; }

    public string? Notes { get; set; }
}
