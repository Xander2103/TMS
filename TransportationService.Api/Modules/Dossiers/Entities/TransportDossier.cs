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
public class TransportDossier : AuditableTenantEntity, IVersionedEntity
{
    public string DossierNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Required for dossiers created since the dossier-foundation wave; legacy rows may be null.</summary>
    public Guid? CustomerId { get; set; }
    public Guid? ResponsibleUserId { get; set; }

    /// <summary>The customer's own reference for this case (PO, project code, ...).</summary>
    public string? CustomerReference { get; set; }

    /// <summary>Business date of the case; null only on pre-wave legacy rows (fall back to CreatedAt).</summary>
    public DateOnly? DossierDate { get; set; }

    /// <summary>
    /// Selling own company, inherited from the customer default (else tenant default) at
    /// creation. Changing it afterwards is permission-gated and audited old→new.
    /// </summary>
    public Guid? LegalEntityId { get; set; }

    public DossierStatus Status { get; set; } = DossierStatus.Open;
    public DateTime? ClosedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (Trip pattern): bumped by the service on every mutation,
    /// echoed by clients; a mismatch yields HTTP 409 carrying the current state. Null from a
    /// client skips the check (legacy/EDI callers).
    /// </summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Set only on wrapper dossiers created by the backfill migration / auto-wrap for a
    /// pre-existing order. Doubles as the idempotency key: the filtered unique index makes a
    /// second wrapper for the same order impossible.
    /// </summary>
    public Guid? OriginTransportOrderId { get; set; }

    public List<DossierActivity> Activities { get; set; } = [];
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
