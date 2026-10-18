using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.CustomerPortal.Entities;

/// <summary>
/// One message in a customer/staff conversation thread. A thread is identified by
/// (CustomerId, TransportOrderId): TransportOrderId null = the customer's general thread,
/// set = a thread scoped to one order. No attachments in v1 (deferred — see task report).
/// </summary>
public class CustomerMessage : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }

    /// <summary>Null = general thread; set = thread scoped to one order.</summary>
    public Guid? TransportOrderId { get; set; }

    /// <summary>The Identity user who wrote this message (portal user or staff member).</summary>
    public Guid AuthorUserId { get; set; }

    /// <summary>True when authored by internal staff; false when authored by a portal user.</summary>
    public bool AuthorIsStaff { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// A planner's "Info opvragen" question on a portal request. The question IS this staff message
    /// (no separate entity): it stays open until the customer writes on the same order thread, and
    /// the customer-facing status reads "Informatie gevraagd" meanwhile (PortalOrderStatusRules).
    /// </summary>
    public bool IsInfoRequest { get; set; }
}

/// <summary>
/// Per-user, per-thread read marker (thread = CustomerId[, TransportOrderId]). Drives unread
/// counts on both sides: a message counts as unread for a user when it was authored by "the
/// other side" and its CreatedAt is after that user's marker for the same thread (or no marker
/// exists yet, meaning nothing in the thread has ever been read).
/// </summary>
public class CustomerMessageRead : AuditableTenantEntity
{
    public Guid UserId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? TransportOrderId { get; set; }
    public DateTime LastReadAt { get; set; }
}
