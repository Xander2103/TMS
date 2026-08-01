using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

public enum ReorderProposalStatus
{
    Proposed,
    Reviewed,
    Approved,
    Ordered,
    Dismissed,
    Completed,
}

/// <summary>
/// A reorder suggestion for a stock target (template or variant): target level − current
/// stock, rounded up to the pack size. Deliberately NOT a purchase order — it is the
/// reviewable precursor a future purchasing module can consume. At most one open proposal
/// per target (filtered unique index).
/// </summary>
public class ReorderProposal : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid? VariantId { get; set; }

    public int CurrentStockSnapshot { get; set; }
    public int? TargetStockSnapshot { get; set; }
    public int SuggestedQuantity { get; set; }
    public int? ApprovedQuantity { get; set; }

    public ReorderProposalStatus Status { get; set; } = ReorderProposalStatus.Proposed;
    public string? Notes { get; set; }

    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
