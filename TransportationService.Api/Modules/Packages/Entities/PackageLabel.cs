using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Packages.Entities;

public enum LabelFormat
{
    Thermal100x150,
    A4,
}

/// <summary>
/// One generated label VERSION: the SnapshotJson freezes every printed field at generation
/// time, and PDFs are always re-rendered from the snapshot — later changes to customer or
/// order data never alter what a historical label said.
/// </summary>
public class PackageLabel : AuditableTenantEntity
{
    public Guid PackageId { get; set; }
    public int Version { get; set; } = 1;
    public LabelFormat Format { get; set; } = LabelFormat.Thermal100x150;

    /// <summary>Serialized LabelSnapshot (camelCase JSON) — the immutable print content.</summary>
    public string SnapshotJson { get; set; } = "{}";

    public DateTime PrintedAt { get; set; }
    public Guid? PrintedByUserId { get; set; }

    /// <summary>Reason for reprints; null for the first print.</summary>
    public string? ReprintReason { get; set; }
}
