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

    public string NameSnapshot { get; set; } = string.Empty;
    public string CategorySnapshot { get; set; } = "Algemeen";

    public IssuedItemStatus Status { get; set; } = IssuedItemStatus.NotIssued;

    public DateOnly? IssuedDate { get; set; }
    public int Quantity { get; set; } = 1;
    public string? SerialNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? IssuedByUserId { get; set; }

    public DateOnly? ReturnedDate { get; set; }
    public string? ReturnCondition { get; set; }
    public Guid? ReceivedBackByUserId { get; set; }
}
