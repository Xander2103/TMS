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
/// Customer invoice built from completed transport orders plus optional manual lines.
/// Totals are always computed from the lines; nothing is denormalised. Sending to an
/// accounting package / Peppol is out of scope — clean extension point only.
/// </summary>
public class Invoice : AuditableTenantEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;

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

    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public string Currency { get; set; } = "EUR";

    public string? Notes { get; set; }

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
    public int Year { get; set; }
    public int Month { get; set; }
    public int NextValue { get; set; } = 1;
}

/// <summary>One invoice line; order-backed lines reference their transport order, manual lines don't.</summary>
public class InvoiceLine : AuditableTenantEntity
{
    public Guid InvoiceId { get; set; }

    public Guid? TransportOrderId { get; set; }

    public int Sequence { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal VatRatePercent { get; set; }
}
