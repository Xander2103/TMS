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

/// <summary>
/// A file attached to a transport order (customer delivery note, CMR, ...). Metadata is
/// created first; the binary is attached through the upload endpoint (same two-step model
/// as fleet documents).
/// </summary>
public class TransportOrderDocument : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }
    public TransportOrderDocumentType DocumentType { get; set; } = TransportOrderDocumentType.Other;
    public string? CustomTypeName { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Opaque storage key (IFileStorageService); null until a file is attached.</summary>
    public string? DocumentPath { get; set; }

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public DateOnly? IssueDate { get; set; }
    public string? Notes { get; set; }
}
