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
    int LineCount,
    InvoiceKind Kind = InvoiceKind.Invoice);

public record InvoiceLineDto(
    Guid Id,
    int Sequence,
    Guid? TransportOrderId,
    string? OrderNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRatePercent,
    decimal LineTotal,
    /// <summary>Verkoopcategorie: live while Draft, frozen name/account after Send (§7.3).</summary>
    Guid? SalesCategoryId = null,
    string? SalesCategoryName = null,
    string? LedgerAccountNumber = null,
    string? LedgerAccountName = null,
    /// <summary>Draft-stage warning when the category is missing or unmapped; null = ok.</summary>
    string? LedgerWarning = null,
    /// <summary>UN/ECE rec 20 unit code for the quantity (default C62 = stuk).</summary>
    string UnitCode = "C62",
    /// <summary>UNCL5305 VAT category: frozen after Send, live-derived while Draft.</summary>
    string? VatCategoryCode = null,
    /// <summary>Fiscal treatment of this line (VatTreatment name): snapshot after Send, live while Draft.</summary>
    string? VatTreatment = null,
    /// <summary>Where the treatment came from (FiscalTreatmentSource name): LineOverride, SalesCode, Customer, TenantDefault.</summary>
    string? VatTreatmentSource = null,
    /// <summary>Statutory text printed for an exempt/reverse-charged line.</summary>
    string? VatLegalText = null,
    /// <summary>The sales code (article code) on the line: snapshot after Send, live while Draft.</summary>
    string? SalesCode = null);

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
    string? PurchaseOrderNumber = null,
    InvoiceKind Kind = InvoiceKind.Invoice,
    Guid? CreditedInvoiceId = null,
    string? CreditedInvoiceNumber = null,
    string? PaymentReference = null,
    /// <summary>The customer treatment frozen on the invoice header (VatTreatment name).</summary>
    string? CustomerVatTreatment = null,
    /// <summary>Document language frozen at creation.</summary>
    string? LanguageCode = null,
    /// <summary>Statutory text for the header treatment, when it has one.</summary>
    string? VatLegalText = null);

/// <summary>Completed, not-yet-invoiced order offered in the invoice builder.</summary>
public record UninvoicedOrderDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    string GoodsDescription,
    string? FirstLoadingCity,
    string? LastUnloadingCity,
    decimal? AgreedPrice,
    /// <summary>The order's issuing entity; null on pre-entity legacy orders (invoiceable under any entity).</summary>
    Guid? LegalEntityId = null,
    /// <summary>Wave 2 §6: NotReady | ReadyForInvoice | ReviewRequired — informational, never a gate.</summary>
    string InvoiceReadiness = "NotReady",
    /// <summary>Semicolon-separated reason codes when ReviewRequired (e.g. pricing.stale;pod.missing).</summary>
    string? InvoiceReadinessReasons = null);

public record ManualInvoiceLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal? VatRatePercent,
    Guid? SalesCategoryId = null,
    /// <summary>UN/ECE rec 20 unit code; null = C62 (stuk).</summary>
    string? UnitCode = null);

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
    string? PurchaseOrderNumber = null,
    string? PaymentReference = null);

/// <summary>Draft-only manual invoice-number correction; requires invoices.override_number + reason.</summary>
public record OverrideInvoiceNumberRequest(string InvoiceNumber, string Reason);

public record InvoiceNumberPreviewDto(string InvoiceNumber, Guid LegalEntityId, int Year, int Month);

/// <summary>Existing lines keep their id; new manual lines come without one. Order-backed lines cannot be added here.</summary>
public record UpdateInvoiceLineInput(
    Guid? Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRatePercent,
    /// <summary>The line's sales category; null explicitly clears it (the editor round-trips the current value).</summary>
    Guid? SalesCategoryId = null,
    /// <summary>UN/ECE rec 20 unit code; null keeps the current value (new lines: C62).</summary>
    string? UnitCode = null);

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
