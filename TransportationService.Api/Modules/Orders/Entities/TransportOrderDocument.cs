using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

public enum TransportOrderDocumentType
{
    /// <summary>Leverbon aangeleverd door de klant.</summary>
    CustomerDeliveryNote,

    /// <summary>Gegenereerde/eigen leverbon.</summary>
    DeliveryNote,

    Cmr,
    Other,
}

/// <summary>D6: where a document hangs — on the dossier as a whole, or on one of its orders.</summary>
public enum DossierDocumentScope
{
    Dossier,
    Order,
}

/// <summary>
/// A file attached to a transport order (customer delivery note, CMR, ...) or — D6, master sprint
/// 2026-09-21 — to a dossier as a whole. Metadata is created first; the binary is attached through
/// the upload endpoint (same two-step model as fleet documents). ONE entity, ONE file: a document
/// is never copied between levels, only its link changes.
/// </summary>
public class TransportOrderDocument : AuditableTenantEntity
{
    /// <summary>The order the document hangs on; NULL = a document of the dossier as a whole.</summary>
    public Guid? TransportOrderId { get; set; }

    /// <summary>
    /// D6: the dossier the document belongs to. For an ORDER document this always equals the owning
    /// dossier of its order (<c>OwningDossierResolver</c>) and follows it when order↔dossier links
    /// change; NULL only for an order document whose order sits in no dossier. A check constraint
    /// guarantees that at least one of the two links is set.
    /// </summary>
    public Guid? DossierId { get; set; }

    public DossierDocumentScope Scope =>
        TransportOrderId is null ? DossierDocumentScope.Dossier : DossierDocumentScope.Order;

    public TransportOrderDocumentType DocumentType { get; set; } = TransportOrderDocumentType.Other;
    public string? CustomTypeName { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Opaque storage key (IFileStorageService); null until a file is attached.</summary>
    public string? DocumentPath { get; set; }

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public DateOnly? IssueDate { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Opt-in publication to the customer portal. Order documents are INTERNAL by default —
    /// damage photos, internal delivery notes and scans attached by planners must never be
    /// published to the customer merely because they hang on that customer's order. The uploader
    /// sets this deliberately through the existing save endpoints; PortalDocumentService filters
    /// on it in both the list and the content download.
    /// </summary>
    public bool CustomerVisible { get; set; }
}
