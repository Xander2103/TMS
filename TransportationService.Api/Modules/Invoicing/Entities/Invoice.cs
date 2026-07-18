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

    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public string Currency { get; set; } = "EUR";

    public string? Notes { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
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
