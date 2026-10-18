using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

public record PortalInvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    decimal Total,
    string Currency,
    /// <summary>Placeholder for the Peppol transmission status; always null until Phase 13 wires
    /// real Peppol send tracking onto the invoice/portal read model.</summary>
    string? PeppolStatus = null,
    /// <summary>"Invoice" or "CreditNote" — credit notes carry positive amounts; the sign is the kind.</summary>
    string Kind = "Invoice");

public record PortalInvoiceLineDto(
    string Description, decimal Quantity, decimal UnitPrice, decimal VatRatePercent, decimal LineTotal);

public record PortalInvoiceAttachmentDto(Guid Id, string FileName, long SizeBytes);

public record PortalInvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    string Currency,
    string? PurchaseOrderNumber,
    IReadOnlyList<PortalInvoiceLineDto> Lines,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total,
    IReadOnlyList<PortalInvoiceAttachmentDto> Attachments,
    /// <summary>See <see cref="PortalInvoiceListItemDto.PeppolStatus"/>.</summary>
    string? PeppolStatus = null,
    /// <summary>See <see cref="PortalInvoiceListItemDto.Kind"/>.</summary>
    string Kind = "Invoice")
{
    /// <summary>KP-09: for a credit note, the invoice it credits (number as issued).</summary>
    public string? CreditedInvoiceNumber { get; init; }
    public Guid? CreditedInvoiceId { get; init; }

    /// <summary>KP-09: for an invoice, the ISSUED credit notes against it — so the customer does not pay
    /// what was credited. Draft credit notes are internal and never listed.</summary>
    public IReadOnlyList<PortalInvoiceReferenceDto> CreditedBy { get; init; } = [];
}

public record PortalInvoiceReferenceDto(Guid Id, string InvoiceNumber);
