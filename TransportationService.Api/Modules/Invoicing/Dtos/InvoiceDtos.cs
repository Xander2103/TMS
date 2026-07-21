using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.Invoicing.Dtos;

public record InvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    Guid CustomerId,
    string CustomerName,
    InvoiceStatus Status,
    string Currency,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total,
    int LineCount);

public record InvoiceLineDto(
    Guid Id,
    int Sequence,
    Guid? TransportOrderId,
    string? OrderNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRatePercent,
    decimal LineTotal);

public record InvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    Guid CustomerId,
    string CustomerName,
    string? CustomerVatNumber,
    InvoiceStatus Status,
    string Currency,
    string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total,
    IReadOnlyList<InvoiceStatus> AllowedTransitions,
    Guid? LegalEntityId,
    string? LegalEntityName,
    int InvoicePeriodYear,
    int InvoicePeriodMonth,
    bool NumberIsManual,
    string? PurchaseOrderNumber = null);

/// <summary>Completed, not-yet-invoiced order offered in the invoice builder.</summary>
public record UninvoicedOrderDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    string GoodsDescription,
    string? FirstLoadingCity,
    string? LastUnloadingCity,
    decimal? AgreedPrice);

public record ManualInvoiceLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal? VatRatePercent);

public record CreateInvoiceRequest(
    Guid CustomerId,
    DateOnly? InvoiceDate,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<ManualInvoiceLineInput> ManualLines,
    string? Notes,
    Guid? LegalEntityId = null,
    int? InvoicePeriodYear = null,
    int? InvoicePeriodMonth = null,
    string? PurchaseOrderNumber = null);

public record UpdateInvoiceRequest(
    DateOnly InvoiceDate,
    DateOnly DueDate,
    IReadOnlyList<UpdateInvoiceLineInput> Lines,
    string? Notes,
    int? InvoicePeriodYear = null,
    int? InvoicePeriodMonth = null,
    string? PurchaseOrderNumber = null);

/// <summary>Draft-only manual invoice-number correction; requires invoices.override_number + reason.</summary>
public record OverrideInvoiceNumberRequest(string InvoiceNumber, string Reason);

public record InvoiceNumberPreviewDto(string InvoiceNumber, Guid LegalEntityId, int Year, int Month);

/// <summary>Existing lines keep their id; new manual lines come without one. Order-backed lines cannot be added here.</summary>
public record UpdateInvoiceLineInput(
    Guid? Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRatePercent);

public record ChangeInvoiceStatusRequest(InvoiceStatus Status);

public enum InvoiceOperationOutcome
{
    Success,
    NotFound,
    InvalidReference,
    InvalidState,
    ValidationFailed,
}

public record InvoiceOperationResult(InvoiceOperationOutcome Outcome, InvoiceDetailDto? Invoice, string? Error = null)
{
    public static InvoiceOperationResult Success(InvoiceDetailDto invoice) => new(InvoiceOperationOutcome.Success, invoice);
    public static readonly InvoiceOperationResult NotFound = new(InvoiceOperationOutcome.NotFound, null);
    public static InvoiceOperationResult InvalidReference(string error) => new(InvoiceOperationOutcome.InvalidReference, null, error);
    public static InvoiceOperationResult InvalidState(string error) => new(InvoiceOperationOutcome.InvalidState, null, error);
    public static InvoiceOperationResult Invalid(string error) => new(InvoiceOperationOutcome.ValidationFailed, null, error);
}
