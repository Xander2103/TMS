using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>
/// A configurable balance "bucket" (e.g. Wettelijk verlof, ADV, Recuperatie). Leave types
/// that deduct point at one of these; each employee has a yearly entitlement per balance type.
/// </summary>
public class LeaveBalanceType : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
