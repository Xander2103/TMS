using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Common.Lookups;

namespace TransportationService.Api.Modules.Tasks.Entities;

public enum EmployeeTaskStatus
{
    Todo,
    InProgress,
    Blocked,
    WaitingForReview,
    Completed,
    Cancelled,
}

public enum TaskPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

/// <summary>Tenant-managed task category (kleur voor de UI, seeded met standaardset).</summary>
public class TaskCategory : LookupEntity
{
    /// <summary>Hex colour (#rrggbb) for badges; null = neutral.</summary>
    public string? Color { get; set; }
}

/// <summary>
/// One task for one responsible employee (assigning to N employees creates N tasks sharing a
/// BatchId — accountability per person, no assignment join table). The status machine in
/// TaskStatusMachine is the only legal transition source; Version is the optimistic-
/// concurrency token on every status action.
/// </summary>
public class EmployeeTask : AuditableTenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Frozen category name so history survives category renames/deletes.</summary>
    public string? CategorySnapshot { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public EmployeeTaskStatus Status { get; set; } = EmployeeTaskStatus.Todo;

    public DateTime? StartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public Guid AssignedEmployeeId { get; set; }

    /// <summary>Groups the sibling tasks of one multi-assign action.</summary>
    public Guid? BatchId { get; set; }

    public bool RequiresReview { get; set; }
    public bool RequiresCompletionNote { get; set; }
    public bool RequiresEvidence { get; set; }

    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }

    public string? BlockedReason { get; set; }
    public string? CompletionNote { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? ReopenedAt { get; set; }

    /// <summary>Set when the task was generated from a recurrence (with its dedupe key).</summary>
    public Guid? RecurrenceId { get; set; }
    public string? RecurrenceDedupeKey { get; set; }

    /// <summary>Set once when the task first became overdue and was announced (anti-spam).</summary>
    public DateTime? OverdueNotifiedAt { get; set; }

    /// <summary>Set when the "due soon" reminder went out (anti-spam).</summary>
    public DateTime? DueSoonNotifiedAt { get; set; }

    /// <summary>Optimistic-concurrency token; bumped on every mutation.</summary>
    public int Version { get; set; }
}

/// <summary>Evidence/attachment on a task; bytes live in file storage (upload pattern H5).</summary>
public class TaskAttachment : AuditableTenantEntity
{
    public Guid TaskId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? Note { get; set; }
}

/// <summary>Reusable checklist of task definitions (onboarding, periodieke controle, …).</summary>
public class TaskTemplate : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class TaskTemplateItem : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }
    public int SortOrder { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>Deadline relative to the apply/generation moment (null = no deadline).</summary>
    public int? DueInDays { get; set; }

    public bool RequiresReview { get; set; }
    public bool RequiresCompletionNote { get; set; }
    public bool RequiresEvidence { get; set; }
}

public enum TaskRecurrenceInterval
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
    CustomDays,
}

/// <summary>
/// Schedule that materialises a template into concrete tasks. Generated tasks snapshot the
/// template content and stay untouched when the template changes later; the per-period
/// RecurrenceDedupeKey (unique per tenant) makes generation idempotent.
/// </summary>
public class TaskRecurrence : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid AssignedEmployeeId { get; set; }

    public TaskRecurrenceInterval Interval { get; set; }

    /// <summary>Interval size for CustomDays (e.g. every 10 days); ignored otherwise.</summary>
    public int? CustomIntervalDays { get; set; }

    /// <summary>First date (inclusive) a period may be generated for.</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Last period start that was generated (bookkeeping; dedupe key is authoritative).</summary>
    public DateOnly? LastGeneratedPeriod { get; set; }
}
