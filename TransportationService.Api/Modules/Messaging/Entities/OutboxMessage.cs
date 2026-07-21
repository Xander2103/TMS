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

    public static readonly IReadOnlyList<string> All =
    [
        OrderConfirmation, TimeWindowConfirmation, DriverEnRoute, EtaUpdate, Delay, DeliveryCompleted,
        PodAvailable, LeaveSubmitted, LeaveApproved, LeaveRejected, PlanningChanged, QualificationExpiry,
        HrBirthday, HrSeniority, HrEmploymentEnd,
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

/// <summary>Tenant-specific template override for (kind, channel, language); built-ins cover the rest.</summary>
public class MessageTemplate : AuditableTenantEntity
{
    public string Kind { get; set; } = string.Empty;
    public MessageChannel Channel { get; set; }
    public string Language { get; set; } = "nl";

    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
