using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Warehousing.Entities;

/// <summary>
/// Wave 4 §1: one storage location inside a warehouse. Two-level convention (warehouse →
/// zone → position) via the self-referencing ParentId, kept deliberately simple and
/// tenant-configurable. Custody events reference locations; Package carries a
/// CurrentWarehouseLocationId projection derived from them.
/// </summary>
public class WarehouseLocation : AuditableTenantEntity
{
    public Guid WarehouseId { get; set; }

    /// <summary>Null = top-level zone; set = position inside the parent zone.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Short scannable/printable code, unique per warehouse (e.g. "A", "A-01").</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>"Zone" | "Position" — display/convention only, the hierarchy is ParentId.</summary>
    public string Kind { get; set; } = "Zone";

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
