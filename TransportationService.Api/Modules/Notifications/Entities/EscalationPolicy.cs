using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Notifications.Entities;

public enum EscalationKind
{
    NegativeStockUnresolved,
    CriticalStockUnhandled,
    TaskOverdue,
    AcknowledgementMissing,
    ReturnOverdue,
}

/// <summary>
/// One escalation rule per condition kind per tenant: after DelayHours the sweep notifies the
/// holders of TargetPermissionCode once per episode (Notification.DedupeKey dedupes;
/// resolution re-arms). Deliberately a flat, bounded model — no workflow engine, no chains.
/// </summary>
public class EscalationPolicy : AuditableTenantEntity
{
    public EscalationKind Kind { get; set; }

    public int DelayHours { get; set; }

    /// <summary>Permission whose holders receive the escalation (validated against the catalogue).</summary>
    public string TargetPermissionCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
