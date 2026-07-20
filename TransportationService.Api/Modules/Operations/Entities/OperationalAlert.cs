using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Operations.Entities;

public enum AlertSeverity
{
    Information,
    Warning,
    Critical,
}

public enum AlertStatus
{
    Active,
    Acknowledged,
    Resolved,
}

/// <summary>
/// Prioritized operational signal for the control center. Alerts are a DEDUPED projection:
/// <see cref="AlertSyncService"/> upserts by <see cref="DedupeKey"/>, so refreshing never
/// duplicates and a disappeared condition auto-resolves its alert.
/// </summary>
public class OperationalAlert : AuditableTenantEntity
{
    public AlertSeverity Severity { get; set; }

    /// <summary>Functional group: Delay, Pod, Incident, Exception, Maintenance, Inspection, Document.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Producing rule identifier (machine code).</summary>
    public string Source { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>SPA route for the "open" action.</summary>
    public string? LinkPath { get; set; }

    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    /// <summary>Stable identity of the underlying condition; unique per tenant.</summary>
    public string DedupeKey { get; set; } = string.Empty;

    public AlertStatus Status { get; set; } = AlertStatus.Active;

    public Guid? AssignedUserId { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Last sync run that still observed the underlying condition.</summary>
    public DateTime LastSeenAt { get; set; }
}
