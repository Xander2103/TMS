using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>Communication categories a customer can route to specific contacts.</summary>
public enum CustomerCommunicationType
{
    PlanningAlert,
    DeliveryChange,
    DelayNotification,
    EtaUpdate,
    OrderConfirmation,
    Invoice,
    InvoiceReminder,
    GeneralReminder,
    Claims,
    Other,
}

/// <summary>
/// "Communicatie" rule: which customer contacts receive which communication type. Rules
/// link REAL <see cref="CustomerContact"/> rows (never free-text names); the contact's
/// email is authoritative at send time, so address changes propagate automatically.
/// Actual outbound sending plugs in via <c>CustomerCommunicationResolver</c> — the
/// messaging outbox consumes resolved recipients when a sender is implemented.
/// </summary>
public class CustomerCommunicationRule : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }

    public CustomerCommunicationType Type { get; set; }

    /// <summary>Required label when <see cref="Type"/> is Other.</summary>
    public string? CustomTypeLabel { get; set; }

    /// <summary>Only "Email" is supported today; modeled for future channels (sms, portal).</summary>
    public string Channel { get; set; } = "Email";

    /// <summary>Optional CC address (free email, e.g. a mailbox not tied to a contact).</summary>
    public string? CcEmail { get; set; }

    /// <summary>Preferred language override; null = contact/customer language.</summary>
    public string? LanguageCode { get; set; }

    /// <summary>Used when none of the linked contacts has a usable email address.</summary>
    public Guid? FallbackContactId { get; set; }

    public bool IsActive { get; set; } = true;

    public List<CustomerCommunicationRuleContact> Contacts { get; set; } = [];
}

/// <summary>Join: rule → customer contact (real FK, cascade with the rule).</summary>
public class CustomerCommunicationRuleContact : AuditableTenantEntity
{
    public Guid RuleId { get; set; }
    public Guid ContactId { get; set; }
}
