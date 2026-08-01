using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>Structured state of one issued item on the employee checklist.</summary>
public enum IssuedItemStatus
{
    NotIssued,
    Issued,
    Returned,
    Missing,
    Damaged,
}

/// <summary>
/// One issued-item record on an employee's checklist. NameSnapshot/CategorySnapshot are
/// frozen at issue time so editing the template never rewrites history; TemplateId is a
/// soft reference (SetNull) purely for grouping/filtering.
/// </summary>
public class EmployeeIssuedItem : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid? TemplateId { get; set; }

    /// <summary>Soft reference to the issued variant (SetNull); the snapshot keeps history readable.</summary>
    public Guid? VariantId { get; set; }

    /// <summary>Frozen variant label ("M / Zwart") at issue time.</summary>
    public string? VariantSnapshot { get; set; }

    public string NameSnapshot { get; set; } = string.Empty;
    public string CategorySnapshot { get; set; } = "Algemeen";

    public IssuedItemStatus Status { get; set; } = IssuedItemStatus.NotIssued;

    public DateOnly? IssuedDate { get; set; }
    public int Quantity { get; set; } = 1;
    public string? SerialNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? IssuedByUserId { get; set; }

    /// <summary>Loan deadline: when set and passed while still issued, the loan is overdue.</summary>
    public DateOnly? ExpectedReturnDate { get; set; }

    /// <summary>Condition noted at issue time (baseline for damage assessment at return).</summary>
    public string? ConditionAtIssue { get; set; }

    public DateOnly? ReturnedDate { get; set; }
    public string? ReturnCondition { get; set; }

    /// <summary>Persisted structured outcome of the return: good/damaged/lost/disposed.</summary>
    public string? ReturnDisposition { get; set; }

    public Guid? ReceivedBackByUserId { get; set; }

    /// <summary>Set once when the overdue-return reminder went out (anti-spam).</summary>
    public DateTime? OverdueNotifiedAt { get; set; }
}
