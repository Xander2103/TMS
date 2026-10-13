using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>How this customer handles purchase-order numbers on orders/invoices.</summary>
public enum PurchaseOrderPolicy
{
    /// <summary>No PO handling.</summary>
    None,

    /// <summary>PO number may be supplied ("Vrij / optioneel").</summary>
    Optional,

    /// <summary>Invoices cannot be sent without an effective PO number ("Verplicht").</summary>
    Required,
}

/// <summary>
/// Effective-dated PO number of a customer. Customers rotate PO numbers (quarterly,
/// yearly, irregular) — a new period gets a NEW row; existing rows are history and are
/// never overwritten. The effective number for a date = the row covering that date with
/// the latest ValidFrom.
/// </summary>
public class CustomerPurchaseOrderNumber : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }

    public string PoNumber { get; set; } = string.Empty;

    public DateOnly ValidFrom { get; set; }

    /// <summary>Null = open-ended (until a newer row starts).</summary>
    public DateOnly? ValidUntil { get; set; }

    public string? Notes { get; set; }
}
