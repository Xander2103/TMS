using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Peppol.Entities;

/// <summary>
/// One inbound Peppol document (supplier invoice/credit note or status message) landed for
/// manual review. No supplier master exists yet, so any link back to internal records is a
/// free-text reviewer note rather than a foreign key.
/// </summary>
public class PeppolIncomingDocument : AuditableTenantEntity
{
    public PeppolIncomingDocumentKind DocumentKind { get; set; } = PeppolIncomingDocumentKind.SupplierInvoice;

    /// <summary>Combined "scheme:id" Peppol participant identifier of the sender.</summary>
    public string SupplierParticipant { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly? DocumentDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }

    /// <summary>Storage key of the raw payload (category "peppol" on IFileStorageService).</summary>
    public string? PayloadStorageKey { get; set; }
    public string PayloadHash { get; set; } = string.Empty;

    public PeppolIncomingDocumentStatus Status { get; set; } = PeppolIncomingDocumentStatus.Received;

    /// <summary>Provider's own message id — idempotency key preventing duplicate ingestion per tenant.</summary>
    public string ProviderMessageId { get; set; } = string.Empty;

    public string? ReviewNote { get; set; }
}
