using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Messaging.Entities;

/// <summary>
/// Kinds of concrete recipient a <see cref="NotificationRule"/> (or catalog default) can name.
/// "Driver" is deliberately the generic "subject employee of this event" recipient: for order
/// events it resolves the order's assigned driver; for HR events (leave, qualification expiry)
/// it resolves the affected employee themselves. There is no separate "self" type in the fixed
/// vocabulary, so this one covers both — see NotificationEventService for the resolution logic.
/// </summary>
public enum NotificationRecipientType
{
    /// <summary>The customer's primary active contact (fallback: Customer.Email).</summary>
    CustomerPrimaryContact,

    /// <summary>Resolved via the existing CustomerCommunicationRule for the type named in Value
    /// (e.g. "Invoice"); Value must parse as a <c>Partners.Entities.CustomerCommunicationType</c>.</summary>
    CustomerCommunicationRule,

    /// <summary>Every active user holding the permission code named in Value.</summary>
    InternalPermission,

    /// <summary>Every active user in a role whose TemplateCode matches Value.</summary>
    InternalRole,

    /// <summary>A literal e-mail address (Value); no in-app recipient, no owner profile.</summary>
    ExplicitEmail,

    /// <summary>The event's subject employee — see the type's summary above.</summary>
    Driver,
}

/// <summary>One entry in a <see cref="NotificationRule"/>'s recipient list.</summary>
public sealed record RecipientSpec(NotificationRecipientType Type, string? Value);

/// <summary>
/// Per-tenant override of a <see cref="Messaging.Services.NotificationEventCatalog"/> entry: which
/// recipients get notified for an event, on which channels, and whether customers may further
/// restrict it for themselves. Absent row = catalog defaults apply as-is.
/// </summary>
public class NotificationRule : AuditableTenantEntity
{
    /// <summary>One of NotificationEventCatalog's event keys; unique per tenant.</summary>
    public string EventKey { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;

    /// <summary>Reserved for a future paid SMS integration; always false today.</summary>
    public bool SmsEnabled { get; set; }

    /// <summary>JSON-serialised <see cref="RecipientSpec"/> list; null/empty = catalog defaults.</summary>
    public string? RecipientsJson { get; set; }

    /// <summary>Whether a <see cref="CustomerNotificationOverride"/> may disable this event for
    /// one customer specifically. Ignored when the rule itself is disabled.</summary>
    public bool AllowCustomerOverride { get; set; }

    /// <summary>P9: customer-facing mail of this event is held for dispatcher review before
    /// sending. Null = the catalog default (sensitive kinds default to review).</summary>
    public bool? RequiresReview { get; set; }
}

/// <summary>
/// Per-customer opt-out of one notification event (only consulted when the tenant-wide
/// <see cref="NotificationRule.AllowCustomerOverride"/> is true). Enabled null = inherit the
/// tenant rule; false = suppressed for this customer regardless of the tenant rule.
/// </summary>
public class CustomerNotificationOverride : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public string EventKey { get; set; } = string.Empty;

    /// <summary>Null = inherit; a customer override can only ever narrow (never re-enable a
    /// tenant-disabled event).</summary>
    public bool? Enabled { get; set; }
}
