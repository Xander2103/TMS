using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Notifications.Entities;

public enum MessagePriority
{
    Normal,
    High,
    Urgent,
}

/// <summary>
/// An internal person-to-person/role message (HR → employee, office → drivers, …).
/// Distinct from system Notifications (which announce it) and from the outbound
/// email/SMS Messaging outbox: this is the in-app inbox itself.
/// </summary>
public class InternalMessage : AuditableTenantEntity
{
    public Guid SenderUserId { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Optional link to a business entity (order, trip, …) for context.</summary>
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }

    public MessagePriority Priority { get; set; } = MessagePriority.Normal;

    /// <summary>Recipients must explicitly confirm (stronger than read).</summary>
    public bool RequiresAcknowledgement { get; set; }

    /// <summary>Hidden from inboxes before this moment; the sweep announces it when due.</summary>
    public DateTime? VisibleFrom { get; set; }

    /// <summary>Hidden from inboxes after this moment.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Sender asked for e-mail delivery through the outbox (per recipient).</summary>
    public bool EmailRequested { get; set; }

    /// <summary>Set when the in-app notification fan-out ran (send time, or by the sweep for
    /// future-visible messages). Guards against double announcements.</summary>
    public DateTime? NotifiedAt { get; set; }

    /// <summary>Withdrawn by the sender/manager; hidden from every inbox.</summary>
    public DateTime? CancelledAt { get; set; }
}

public class InternalMessageRecipient
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Outbox row that carries the e-mail copy (null = no e-mail requested/possible).</summary>
    public Guid? EmailOutboxMessageId { get; set; }
}
