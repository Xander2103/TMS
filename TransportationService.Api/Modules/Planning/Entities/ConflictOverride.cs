using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Planning.Entities;

/// <summary>
/// Persistent record of a permission-holder overriding blocking operational conflicts
/// (trip planning, dock scheduling). The actor is <see cref="AuditableTenantEntity.CreatedByUserId"/>;
/// the reason is mandatory and the row stays visible in the entity's timeline.
/// </summary>
public class ConflictOverride : AuditableTenantEntity
{
    /// <summary>Aggregate the override applies to: "Trip" or "DockAppointment".</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>Comma-separated machine codes of the conflicts that were overridden.</summary>
    public string ConflictCodes { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }
}
