using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Dossiers.Entities;

public enum DossierStatus
{
    Open,
    Closed,
}

/// <summary>
/// A transport dossier bundles related operational records around one commercial case
/// (project, claim, recurring lane, ...). Orders are linked explicitly; trips, invoices and
/// documents follow through those orders so nothing is duplicated. The DOS- number is
/// claimed from the tenant counter like every other numbered record.
/// </summary>
public class TransportDossier : AuditableTenantEntity
{
    public string DossierNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? CustomerId { get; set; }
    public Guid? ResponsibleUserId { get; set; }

    public DossierStatus Status { get; set; } = DossierStatus.Open;
    public DateTime? ClosedAt { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Link between a dossier and a transport order; one active link per pair.</summary>
public class DossierOrder : AuditableTenantEntity
{
    public Guid DossierId { get; set; }
    public Guid TransportOrderId { get; set; }
}

public enum DossierRelationType
{
    FollowUp,
    Return,
    Claim,
    Replacement,
    Duplicate,
    Other,
}

/// <summary>
/// Directed link between two dossiers of the same tenant. Self-links are refused and the
/// pair+type combination is unique in BOTH directions (service check + filtered DB index).
/// </summary>
public class DossierRelation : AuditableTenantEntity
{
    public Guid SourceDossierId { get; set; }
    public Guid TargetDossierId { get; set; }
    public DossierRelationType RelationType { get; set; }
    public string? Notes { get; set; }
}
