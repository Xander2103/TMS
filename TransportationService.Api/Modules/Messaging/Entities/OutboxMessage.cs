using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Messaging.Entities;

public enum MessageChannel
{
    Email,
    Sms,
}

public enum OutboxStatus
{
    Pending,
    Sent,
    Failed,
    Suppressed,
}

public enum MessageOwnerType
{
    Customer,
    Employee,
}

/// <summary>Machine codes for every supported message kind (wire/template key).</summary>
public static class MessageKinds
{
    public const string OrderConfirmation = "order_confirmation";
    public const string TimeWindowConfirmation = "time_window_confirmation";
    public const string DriverEnRoute = "driver_en_route";
    public const string EtaUpdate = "eta_update";
    public const string Delay = "delay";
    public const string DeliveryCompleted = "delivery_completed";
    public const string PodAvailable = "pod_available";
    public const string LeaveSubmitted = "leave_submitted";
    public const string LeaveApproved = "leave_approved";
    public const string LeaveRejected = "leave_rejected";
    public const string PlanningChanged = "planning_changed";
    public const string QualificationExpiry = "qualification_expiry";
    public const string HrBirthday = "hr_birthday";
    public const string HrSeniority = "hr_seniority";
    public const string HrEmploymentEnd = "hr_employment_end";

    // Notification-event kinds (corrections wave 4, phase 6): one kind per NotificationEventCatalog
    // entry, named identically to the event key so outbox rows and rule resolution line up 1:1.
    public const string OrderCreated = "order_created";
    public const string OrderSubmittedPortal = "order_submitted_portal";
    public const string OrderAccepted = "order_accepted";
    public const string OrderRejected = "order_rejected";
    public const string OrderInfoRequested = "order_info_requested";
    public const string OrderPlanned = "order_planned";
    public const string OrderPickupWindow = "order_pickup_window";
    public const string OrderDeliveryWindow = "order_delivery_window";
    public const string OrderPickupCompleted = "order_pickup_completed";
    public const string OrderDeliveryCompleted = "order_delivery_completed";
    public const string OrderDelayDetected = "order_delay_detected";
    public const string OrderFailedDelivery = "order_failed_delivery";
    public const string OrderDamageRegistered = "order_damage_registered";
    public const string OrderPodAvailable = "order_pod_available";
    public const string InvoiceDraftReady = "invoice_draft_ready";
    public const string InvoiceSent = "invoice_sent";
    public const string InvoicePeppolQueued = "invoice_peppol_queued";
    public const string InvoicePeppolDelivered = "invoice_peppol_delivered";
    public const string InvoicePeppolFailed = "invoice_peppol_failed";
    public const string InvoiceCreditNote = "invoice_credit_note";
    public const string PersonnelQualificationExpiry = "personnel_qualification_expiry";
    public const string PersonnelMedicalExpiry = "personnel_medical_expiry";
    public const string PersonnelDocumentExpiry = "personnel_document_expiry";
    public const string LeaveRequested = "leave_requested";
    public const string LeaveDecided = "leave_decided";
    public const string EmployeeNotePinned = "employee_note_pinned";
    public const string FleetMaintenanceDue = "fleet_maintenance_due";
    public const string FleetInspectionDue = "fleet_inspection_due";
    public const string FleetDocumentExpiry = "fleet_document_expiry";
    public const string FleetDamageCreated = "fleet_damage_created";

    public static readonly IReadOnlyList<string> All =
    [
        OrderConfirmation, TimeWindowConfirmation, DriverEnRoute, EtaUpdate, Delay, DeliveryCompleted,
        PodAvailable, LeaveSubmitted, LeaveApproved, LeaveRejected, PlanningChanged, QualificationExpiry,
        HrBirthday, HrSeniority, HrEmploymentEnd,
        OrderCreated, OrderSubmittedPortal, OrderAccepted, OrderRejected, OrderInfoRequested, OrderPlanned,
        OrderPickupWindow, OrderDeliveryWindow, OrderPickupCompleted, OrderDeliveryCompleted,
        OrderDelayDetected, OrderFailedDelivery, OrderDamageRegistered, OrderPodAvailable,
        InvoiceDraftReady, InvoiceSent, InvoicePeppolQueued, InvoicePeppolDelivered, InvoicePeppolFailed,
        InvoiceCreditNote, PersonnelQualificationExpiry, PersonnelMedicalExpiry, PersonnelDocumentExpiry,
        LeaveRequested, LeaveDecided, EmployeeNotePinned,
        FleetMaintenanceDue, FleetInspectionDue, FleetDocumentExpiry, FleetDamageCreated,
    ];
}

/// <summary>
/// One outbound message in the provider-neutral outbox. Producers only queue; the dispatcher
/// owns delivery, retries with backoff and permanent-failure handling. Suppressed rows keep
/// an audit trail of what was deliberately NOT sent (preference/opt-out) and why.
/// </summary>
public class OutboxMessage : AuditableTenantEntity
{
    public MessageChannel Channel { get; set; }

    /// <summary>One of <see cref="MessageKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public MessageOwnerType OwnerType { get; set; }
    public Guid OwnerId { get; set; }

    public string RecipientAddress { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string Language { get; set; } = "nl";

    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>Natural key preventing duplicate sends (unique per tenant).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }

    /// <summary>Set on the fallback message spawned after a permanent failure (once, never chained).</summary>
    public Guid? FallbackOfMessageId { get; set; }
}

/// <summary>Per-customer/per-employee messaging preferences; absent row means email-on with defaults.</summary>
public class MessagingProfile : AuditableTenantEntity
{
    public MessageOwnerType OwnerType { get; set; }
    public Guid OwnerId { get; set; }

    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; }

    /// <summary>Override address; falls back to the owner's own e-mail.</summary>
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Extra CC-style recipients (JSON array of addresses), each queued separately.</summary>
    public string? ExtraRecipientsJson { get; set; }

    /// <summary>JSON array of enabled <see cref="MessageKinds"/>; null means all kinds.</summary>
    public string? EnabledKindsJson { get; set; }

    public string? PreferredLanguage { get; set; }

    public TimeOnly? QuietHoursFrom { get; set; }
    public TimeOnly? QuietHoursTo { get; set; }

    public MessageChannel? FallbackChannel { get; set; }
}

/// <summary>
/// Tenant-specific template override for (kind, channel, language); built-ins cover the rest.
/// <see cref="CustomerId"/> null = tenant-wide default; set = an override scoped to one customer,
/// consulted before the tenant default (resolution chain lives in <c>MessageOutboxService</c>).
/// </summary>
public class MessageTemplate : AuditableTenantEntity
{
    public string Kind { get; set; } = string.Empty;
    public MessageChannel Channel { get; set; }
    public string Language { get; set; } = "nl";

    /// <summary>Null = tenant-wide default template; set = override for one customer.</summary>
    public Guid? CustomerId { get; set; }

    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>Optional rich-text authoring surface, sanitized on save (see HtmlSanitizer). Not
    /// yet consumed by outbound rendering (plain Body drives the outbox) — reserved for the
    /// admin preview/rich-email pipeline.</summary>
    public string? BodyHtml { get; set; }

    public bool IsActive { get; set; } = true;
}
