using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Notifications.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Entities;

public enum PortalMessageDisplayMode
{
    /// <summary>Regular entry in the portal message feed.</summary>
    Notification,

    /// <summary>Also shown as a banner on the portal dashboard while visible.</summary>
    DashboardBanner,

    /// <summary>Blocks the portal dashboard until the user acknowledges. Reserve for
    /// exceptional announcements; the portal stays navigable apart from the overlay.</summary>
    BlockingAcknowledgement,
}

/// <summary>
/// A staff-authored announcement to customer-portal users, with fixed NL/FR/EN content
/// columns (three known portal languages; display falls back FR/EN → NL). Targeting lives
/// in <see cref="PortalMessageRecipient"/>, per-user read/ack state in
/// <see cref="PortalMessageReceipt"/>. Distinct from CustomerMessage (the two-way thread)
/// and PortalAnnouncement (legacy tenant-wide banner without targeting or receipts).
/// </summary>
public class PortalMessage : AuditableTenantEntity
{
    public string TitleNl { get; set; } = string.Empty;
    public string? TitleFr { get; set; }
    public string? TitleEn { get; set; }

    public string BodyNl { get; set; } = string.Empty;
    public string? BodyFr { get; set; }
    public string? BodyEn { get; set; }

    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public PortalMessageDisplayMode DisplayMode { get; set; } = PortalMessageDisplayMode.Notification;
    public bool RequiresAcknowledgement { get; set; }

    public DateTime? VisibleFrom { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>"order" or "invoice"; only allowed with a single-customer target and validated
    /// to belong to that customer.</summary>
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    public bool EmailRequested { get; set; }
    public DateTime? CancelledAt { get; set; }
}

/// <summary>Target: one customer, optionally narrowed to a single portal user.</summary>
public class PortalMessageRecipient
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PortalMessageId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>Null = every portal user of the customer.</summary>
    public Guid? UserId { get; set; }
}

/// <summary>Per portal user read/acknowledge state (created lazily on first read/ack).</summary>
public class PortalMessageReceipt
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PortalMessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
