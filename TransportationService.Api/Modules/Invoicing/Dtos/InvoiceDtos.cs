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
    InvoiceKind Kind = InvoiceKind.Invoice,
    // ACC-M10: accounting productivity columns/filters (customer number, newest Peppol
    // transmission status, paid moment) and the number as shown (legal number, or the draft
    // reference while no legal number exists yet).
    string? CustomerNumber = null,
    string? PeppolStatus = null,
    DateTime? PaidAt = null,
    string? DisplayNumber = null);

public record InvoiceLineDto(
    Guid Id,
    int Sequence,
    Guid? TransportOrderId,
    string? OrderNumber,
    string Description,
    /// <summary>Up to 3 decimals.</summary>
    decimal Quantity,
    /// <summary>Up to 4 decimals (ACC-M2); the line total is what rounds to cents.</summary>
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
    string? SalesCode = null,
    /// <summary>
    /// What the CUSTOMER reads for this line in the invoice language. While Draft this is what
    /// Send will freeze into <see cref="Description"/> (same rule, see InvoiceLineDescriptions);
    /// after Send it equals <see cref="Description"/>.
    /// </summary>
    string? CustomerDescription = null,
    /// <summary>The standalone dossier activity this line bills (traceability); null otherwise.</summary>
    Guid? DossierActivityId = null,
    string? DossierNumber = null,
    /// <summary>Human unit ("uur", "Europallet") next to the structured <see cref="UnitCode"/>.</summary>
    string? UnitLabel = null);

public record InvoiceDetailDto(
    Guid Id,
    /// <summary>Null while Draft — the legal number is claimed at issue (ACC-H3).</summary>
    string? InvoiceNumber,
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
    string? VatLegalText = null,
    /// <summary>Credit notes issued against THIS invoice (any status), so the relation is visible both ways.</summary>
    IReadOnlyList<InvoiceReferenceDto>? CreditNotes = null,
    /// <summary>"CONCEPT-A3FCFE3E" while Draft (ACC-H3).</summary>
    string? DraftReference = null,
    /// <summary>The number once issued, else the draft reference — what screens print.</summary>
    string? DisplayNumber = null,
    /// <summary>The VAT rate a manual line defaults to under this invoice's treatment (ACC-H1).</summary>
    decimal? DefaultVatRatePercent = null,
    /// <summary>Effective payment term (customer → entity → tenant) the due date follows (ACC-H5).</summary>
    int? PaymentTermDays = null,
    bool DueDateIsManual = false,
    DateTime? IssuedAt = null,
    DateTime? PaidAt = null,
    string? PaidByName = null,
    string? CreditReason = null,
    /// <summary>Frozen party presentation (issued) or the current master data (draft preview).</summary>
    string? CustomerLegalName = null,
    string? CustomerAddressLine = null,
    string? SellerLegalName = null,
    string? SellerCompanyNumber = null,
    string? PaymentTermsText = null);

/// <summary>A related document reference (original ↔ credit note).</summary>
public record InvoiceReferenceDto(Guid Id, string? InvoiceNumber, InvoiceStatus Status, string? DisplayNumber = null);

/// <summary>ACC-M4/H6: one reason an invoice cannot be issued yet, with the lines it concerns.</summary>
public record InvoiceIssueIssueDto(string Code, string Message, IReadOnlyList<Guid> LineIds);

/// <summary>Issue preflight: the SAME rules the Draft → Sent transition enforces.</summary>
public record InvoiceIssuePreflightDto(bool CanIssue, IReadOnlyList<InvoiceIssueIssueDto> Issues);

/// <summary>ACC-M11: crediting is a deliberate, motivated act.</summary>
public record CreateCreditNoteRequest(string? Reason);

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
    string? InvoiceReadinessReasons = null,
    /// <summary>ACC-M1: explicitly free transport (resolved € 0), never confused with "no price".</summary>
    bool IsFree = false,
    /// <summary>ACC-B1: only ReadyForInvoice orders can be put on an invoice; the rest is listed for context.</summary>
    bool IsInvoiceable = false);

/// <summary>
/// Closure sprint 2026-09-23: a priced STANDALONE dossier activity (no linked order) of the
/// customer that is not yet on a live invoice — the activity-side twin of
/// <see cref="UninvoicedOrderDto"/> for the invoice builder.
/// </summary>
public record UninvoicedActivityDto(
    Guid Id,
    Guid DossierId,
    string DossierNumber,
    string? DossierTitle,
    string ActivityTypeName,
    string? Label,
    DateOnly? PlannedDate,
    /// <summary>The agreed total (OneOff amount or the sum of the price lines); € 0 only when explicitly free.</summary>
    decimal AgreedPrice,
    /// <summary>Explicitly confirmed free (€ 0 by decision): invoiced as an explicit zero line.</summary>
    bool IsFree,
    /// <summary>"OneOff" or "Lines" — how many invoice lines the activity will produce.</summary>
    string PricingSource,
    int PriceLineCount,
    /// <summary>The dossier's issuing entity; null when the dossier has none (invoiceable under any entity).</summary>
    Guid? LegalEntityId = null);

public record ManualInvoiceLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    /// <summary>Null = the rate the invoice's VAT treatment dictates (ACC-H1); never a silent 0.</summary>
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
    string? PurchaseOrderNumber = null,
    /// <summary>Standalone dossier activities to bill (see <see cref="UninvoicedActivityDto"/>); null = none.</summary>
    IReadOnlyList<Guid>? DossierActivityIds = null);

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

/// <summary>The number the NEXT issued document would get (informative; nothing is claimed until issue).</summary>
public record InvoiceNumberPreviewDto(string InvoiceNumber, Guid LegalEntityId, int Year, int Month);

/// <summary>Existing lines keep their id; new manual lines come without one. Order-backed lines cannot be added here.</summary>
public record UpdateInvoiceLineInput(
    Guid? Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    /// <summary>Null = the rate the invoice's VAT treatment dictates (ACC-H1).</summary>
    decimal? VatRatePercent,
    /// <summary>The line's sales category; null explicitly clears it (the editor round-trips the current value).</summary>
    Guid? SalesCategoryId = null,
    /// <summary>UN/ECE rec 20 unit code; null keeps the current value (new lines: C62).</summary>
    string? UnitCode = null,
    /// <summary>Human unit label; null keeps the current value.</summary>
    string? UnitLabel = null);

/// <param name="PaidOn">UX-013: the day the money came in, for <c>Paid</c> only; empty = today.</param>
public record ChangeInvoiceStatusRequest(InvoiceStatus Status, DateOnly? PaidOn = null);

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
