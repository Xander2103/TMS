namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

/// <summary>Where a portal-visible document's binary actually lives — drives which table/storage
/// key the content endpoint reads from.</summary>
public enum PortalDocumentSource
{
    OrderDocument,
    Pod,
    InvoiceAttachment,
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
    string? InvoiceNumber);
