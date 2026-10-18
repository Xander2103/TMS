namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

/// <summary>Where a portal-visible document's binary actually lives — drives which table/storage
/// key the content endpoint reads from.</summary>
public enum PortalDocumentSource
{
    OrderDocument,
    Pod,
    InvoiceAttachment,
    /// <summary>KP-06: an issued CMR / delivery note / work order a planner published, rendered from its frozen snapshot.</summary>
    IssuedTransportDocument,
}

public record PortalDocumentDto(
    Guid Id,
    PortalDocumentSource Source,
    string Title,
    string? FileName,
    DateTime CreatedAt,
    Guid? OrderId,
    string? OrderNumber,
    Guid? InvoiceId,
    string? InvoiceNumber,
    /// <summary>D6: set for a published document of a dossier as a whole (Source OrderDocument, no order).</summary>
    string? DossierNumber = null);
