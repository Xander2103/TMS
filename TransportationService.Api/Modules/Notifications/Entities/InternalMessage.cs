using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Notifications.Entities;

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
}

public class InternalMessageRecipient
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime? ReadAt { get; set; }
}
