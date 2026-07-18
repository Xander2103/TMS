using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// Base class for simple tenant-configurable reference lists (departments, job functions,
/// vehicle/trailer/driver/customer categories, ...). Each row is identified by a
/// tenant-unique <see cref="Code"/> and carries a display <see cref="Name"/>. Anything more
/// specialised than this belongs in its own entity rather than a lookup.
/// </summary>
public abstract class LookupEntity : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Controls the order lookups are presented in dropdowns; lower comes first.</summary>
    public int SortOrder { get; set; }
}
