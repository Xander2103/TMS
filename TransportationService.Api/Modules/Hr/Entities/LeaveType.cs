using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>
/// A configurable leave/absence type (Wettelijk verlof, ADV, Ziekte, …). Drives per-request
/// validation and whether/which balance an absence deducts. Maps to the existing
/// <see cref="AbsenceType"/> enum so planning, history and reporting keyed on that keep working.
/// </summary>
public class LeaveType : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPaid { get; set; } = true;

    /// <summary>When true, approved/pending absences of this type reduce <see cref="BalanceTypeId"/>.</summary>
    public bool DeductsFromBalance { get; set; }
    /// <summary>Required when <see cref="DeductsFromBalance"/> is true.</summary>
    public Guid? BalanceTypeId { get; set; }

    /// <summary>The existing absence-type kind this configurable type maps to.</summary>
    public AbsenceType AbsenceType { get; set; } = AbsenceType.Other;

    public bool RequiresApproval { get; set; } = true;
    public bool AllowsHalfDays { get; set; } = true;
    public bool RequiresReason { get; set; }
    public bool RequiresAttachment { get; set; }
    public bool VisibleInSelfService { get; set; } = true;

    /// <summary>Planning colour (hex, e.g. "#2563eb").</summary>
    public string? Colour { get; set; }
    public int SortOrder { get; set; }
}
