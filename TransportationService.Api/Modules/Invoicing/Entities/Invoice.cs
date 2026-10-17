using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Invoicing.Entities;

public enum InvoiceStatus
{
    Draft,
    Sent,
    Paid,
    Cancelled,
}

/// <summary>
/// Invoice vs credit note. A credit note stores POSITIVE line amounts (like the UBL 2.1
/// CreditNote document it maps to); its commercial sign comes from the Kind, never from
/// negative quantities or prices.
/// </summary>
public enum InvoiceKind
{
    Invoice = 0,
    CreditNote = 1,
}

/// <summary>
/// Customer invoice built from completed transport orders plus optional manual lines.
/// Totals are always computed from the lines; nothing is denormalised. Sending to an
/// accounting package / Peppol is out of scope — clean extension point only.
/// </summary>
public class Invoice : AuditableTenantEntity
{
    /// <summary>
    /// The LEGAL document number. Null while Draft: the number is claimed atomically at issue
    /// (Draft → Sent) from the entity's sequence, so a deleted or cancelled draft never consumes
    /// one and the series stays gap-free. A draft is referred to by <see cref="DraftReference"/>.
    /// </summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>Human reference of a draft ("CONCEPT-A3FCFE3E"), derived from the id — never stored.</summary>
    public string DraftReference => "CONCEPT-" + Id.ToString("N")[..8].ToUpperInvariant();

    /// <summary>The number once issued, otherwise the draft reference.</summary>
    public string DisplayNumber => InvoiceNumber ?? DraftReference;

    /// <summary>When the document was issued (number claimed, snapshot frozen).</summary>
    public DateTime? IssuedAt { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>
    /// Issuing own company. Nullable only for pre-wave historic rows; the service requires
    /// it on every new invoice and on Draft→Sent.
    /// </summary>
    public Guid? LegalEntityId { get; set; }

    /// <summary>
    /// Invoice period (boekmaand) driving the numbering sequence. Defaults to the invoice
    /// date's month; a user may explicitly pick an earlier month (invoicing July in August)
    /// but never a future one. Historic rows are backfilled from InvoiceDate.
    /// </summary>
    public int InvoicePeriodYear { get; set; }
    public int InvoicePeriodMonth { get; set; }

    /// <summary>True when the number was manually corrected (permission + reason, audited).</summary>
    public bool NumberIsManual { get; set; }

    /// <summary>
    /// PO number on the invoice. Defaults from the customer's effective PO (by invoice
    /// date) else the orders' single distinct reference; editable on Draft. When the
    /// customer's PO policy is Required, sending is blocked while this is empty.
    /// </summary>
    public string? PurchaseOrderNumber { get; set; }

    // Seller/customer snapshot: copied from the legal entity + customer at creation and
    // refreshed while Draft. Once Sent, later master-data changes never mutate these: the PDF
    // and the UBL e-invoice render from these fields only (ACC-H4).
    public string? SellerName { get; set; }
    public string? SellerVatNumber { get; set; }
    public string? SellerIban { get; set; }
    public string? SellerAddressLine { get; set; }
    public string? SellerLegalName { get; set; }
    public string? SellerCompanyNumber { get; set; }
    public string? SellerStreet { get; set; }
    public string? SellerHouseNumber { get; set; }
    public string? SellerPostalCode { get; set; }
    public string? SellerCity { get; set; }
    public string? SellerCountryCode { get; set; }
    public string? SellerBic { get; set; }
    public string? SellerPeppolScheme { get; set; }
    public string? SellerPeppolId { get; set; }
    public string? SellerInvoiceFooter { get; set; }
    public string? CustomerVatTreatment { get; set; }
    public string? CustomerVatNumberSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerLegalNameSnapshot { get; set; }
    public string? CustomerCompanyNumberSnapshot { get; set; }
    public string? CustomerStreetSnapshot { get; set; }
    public string? CustomerHouseNumberSnapshot { get; set; }
    public string? CustomerPostalCodeSnapshot { get; set; }
    public string? CustomerCitySnapshot { get; set; }
    public string? CustomerCountryCodeSnapshot { get; set; }
    public string? CustomerPeppolSchemeSnapshot { get; set; }
    public string? CustomerPeppolIdSnapshot { get; set; }
    public string? CustomerBuyerReferenceSnapshot { get; set; }

    /// <summary>
    /// Effective payment term (days) the due date was computed with: customer term → legal
    /// entity term → tenant default (ACC-H5). Frozen with the rest at issue.
    /// </summary>
    public int PaymentTermDays { get; set; } = 30;

    /// <summary>
    /// True once a user saved a due date that differs from invoice date + term; a manual due
    /// date is kept when the invoice date changes, an automatic one is recalculated.
    /// </summary>
    public bool DueDateIsManual { get; set; }

    /// <summary>Manual payment registration (ACC-M6): when and by whom "Betaald" was set.</summary>
    public DateTime? PaidAt { get; set; }
    public Guid? PaidByUserId { get; set; }

    /// <summary>Why a credit note was made (required at creation, audited).</summary>
    public string? CreditReason { get; set; }
    /// <summary>Legal VAT text (e.g. "Btw verlegd ...") from the treatment catalog.</summary>
    public string? VatLegalText { get; set; }

    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public string Currency { get; set; } = "EUR";

    public string? Notes { get; set; }

    public InvoiceKind Kind { get; set; } = InvoiceKind.Invoice;

    /// <summary>
    /// Document language, frozen at creation from Customer.InvoiceLanguageCode ??
    /// DefaultLanguageCode ?? "nl" (Wave 2). Drives the PDF string catalog; later master-data
    /// changes never re-language an existing invoice.
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>For credit notes: the invoice being (partially) credited.</summary>
    public Guid? CreditedInvoiceId { get; set; }

    /// <summary>Optional structured payment reference (e.g. Belgian OGM) printed and sent as PaymentID.</summary>
    public string? PaymentReference { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
}

/// <summary>
/// Per-legal-entity, per-invoice-month numbering counter. NextValue is an optimistic
/// concurrency token: concurrent claims conflict at SaveChanges and retry with a fresh
/// value, so duplicate invoice numbers are impossible. Counters only ever move forward —
/// cancelled or deleted invoices never release their number.
/// </summary>
public class InvoiceSequence : AuditableTenantEntity
{
    public Guid LegalEntityId { get; set; }

    /// <summary>Invoices and credit notes number in their own series (credit-note prefix).</summary>
    public InvoiceKind Kind { get; set; } = InvoiceKind.Invoice;
    public int Year { get; set; }
    public int Month { get; set; }
    public int NextValue { get; set; } = 1;
}

/// <summary>
/// File attached to an invoice (PDF, XML, XLSX, ...). Internal by default; only
/// attachments with IncludeWhenSending are ever meant to accompany an outgoing invoice.
/// Content lives in IFileStorageService (category "invoices"); this row is metadata only.
/// </summary>
public class InvoiceAttachment : AuditableTenantEntity
{
    public Guid InvoiceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public bool IncludeWhenSending { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One invoice line; order-backed lines reference their transport order, manual lines don't.</summary>
public class InvoiceLine : AuditableTenantEntity
{
    public Guid InvoiceId { get; set; }

    public Guid? TransportOrderId { get; set; }

    /// <summary>
    /// Closure sprint 2026-09-23: the standalone dossier activity this line bills (its agreed
    /// price or one of its price lines). Null on order-backed and manual lines; never set
    /// together with <see cref="TransportOrderId"/> — an order-backed activity is invoiced
    /// through its order only.
    /// </summary>
    public Guid? DossierActivityId { get; set; }

    public int Sequence { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal VatRatePercent { get; set; }

    /// <summary>UN/ECE Recommendation 20 unit code for the quantity ("C62" = piece, default).</summary>
    public string UnitCode { get; set; } = "C62";

    /// <summary>Human unit as the customer reads it ("uur", "Europallet"); the structured code above is for e-invoicing.</summary>
    public string? UnitLabel { get; set; }

    /// <summary>
    /// UNCL5305 VAT category (S/Z/AE/K/G/E) frozen at Send from the customer's VAT treatment —
    /// snapshot immutability, like the ledger block below. Null while Draft (derived live).
    /// </summary>
    public string? VatCategoryCode { get; set; }

    // --- Sales category + ledger snapshot (corrections wave §7.3) ---

    /// <summary>Live categorisation while Draft (system role or manual pick); may be null.</summary>
    public Guid? SalesCategoryId { get; set; }

    /// <summary>Frozen at Send: the category name at finalization; never re-derived afterwards.</summary>
    public string? SalesCategoryNameSnapshot { get; set; }

    /// <summary>Frozen at Send from the then-current mapping — later mapping changes never touch this line.</summary>
    public Guid? LedgerAccountId { get; set; }
    public string? LedgerAccountNumberSnapshot { get; set; }
    public string? LedgerAccountNameSnapshot { get; set; }

    // --- Sales code + fiscal snapshot (sprint 5H) ---
    // Everything needed to reproduce this line's commercial and fiscal meaning later, even
    // after the sales code, its translations or its ledger mapping have been edited.

    /// <summary>
    /// The sales code as it read at finalization ("ADM"), independent of later renames. The
    /// code itself is <see cref="SalesCategoryId"/> above — this freezes its printed code.
    /// </summary>
    public string? SalesCodeSnapshot { get; set; }

    /// <summary>The invoice language the customer-facing <see cref="Description"/> was taken in.</summary>
    public string? DescriptionLanguageSnapshot { get; set; }

    /// <summary>The fiscal treatment that applied, as text (e.g. "ReverseCharge").</summary>
    public string? VatTreatmentSnapshot { get; set; }

    /// <summary>Which level decided it: LineOverride | SalesCode | Customer | TenantDefault.</summary>
    public string? VatTreatmentSourceSnapshot { get; set; }

    /// <summary>Statutory wording printed for this line's treatment, frozen with it.</summary>
    public string? VatLegalTextSnapshot { get; set; }

    /// <summary>Cost centre resolved from the sales code / entity mapping at finalization.</summary>
    public string? CostCentreSnapshot { get; set; }

    /// <summary>
    /// An authorised, explicit deviation from the resolved treatment for THIS line. Normal
    /// users never set this; it exists so an exception is recorded as an exception.
    /// </summary>
    public string? VatTreatmentOverride { get; set; }

    /// <summary>Why the override was applied; required when one is set.</summary>
    public string? VatTreatmentOverrideReason { get; set; }
}
