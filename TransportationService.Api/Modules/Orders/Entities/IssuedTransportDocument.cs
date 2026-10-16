using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

public enum IssuedTransportDocumentKind
{
    Cmr,
    DeliveryNote,
    WorkOrder,
}

/// <summary>
/// D6 (master sprint 2026-09-21): a consignment note, delivery note or work order that was ISSUED
/// for an order with a unique own number. Nothing is stored but the identity: the PDF is rendered on
/// demand from the order through the existing renderer. The issuer is <c>CreatedByUserId</c>.
/// <para>
/// M9 (acceptance 2026-10-16): ONE ACTIVE document per order and kind. A corrected CMR is a new
/// document with a new number that explicitly REPLACES the active one (with a reason); the replaced
/// document is kept — never deleted, its number never reused — and points at its successor.
/// </para>
/// </summary>
public class IssuedTransportDocument : AuditableTenantEntity
{
    public const int ReplacementReasonMaxLength = 500;

    /// <summary>Null = active. Set = replaced by that document (same order and kind).</summary>
    public Guid? ReplacedByDocumentId { get; set; }

    public DateTime? ReplacedAt { get; set; }

    /// <summary>Why the document was replaced — mandatory when replacing.</summary>
    public string? ReplacementReason { get; set; }

    public Guid TransportOrderId { get; set; }

    /// <summary>The owning dossier of the order AT ISSUE TIME (null when the order sat in none).</summary>
    public Guid? DossierId { get; set; }

    public IssuedTransportDocumentKind Kind { get; set; }

    /// <summary>Own number, unique per tenant: CMR-2026-00001 / LB-2026-00001 / WB-2026-00001. Never changed.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>A pre-printed / external waybill number as entered by the user. NEVER overwritten by ours.</summary>
    public string? ExternalNumber { get; set; }

    /// <summary>Client idempotency key: the same request (double click, retry) yields the same record.</summary>
    public Guid RequestId { get; set; }

    public DateTime IssuedAt { get; set; }
}

/// <summary>
/// Per-tenant, per-kind, per-year numbering counter of issued transport documents — the
/// <c>InvoiceSequence</c> pattern. NextValue is an optimistic concurrency token: concurrent claims
/// conflict at SaveChanges and retry with a fresh value. Counters only ever move forward; a new
/// year starts a new row, an existing row is never reset.
/// </summary>
public class TransportDocumentSequence : AuditableTenantEntity
{
    public IssuedTransportDocumentKind Kind { get; set; }
    public int Year { get; set; }
    public int NextValue { get; set; } = 1;
}
