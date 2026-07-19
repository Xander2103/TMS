using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

/// <summary>
/// One scannable cargo line of an order (package, pallet, colli). The barcode is unique
/// within the order when present (an ambiguous scan would be unresolvable); across orders
/// the same barcode may recur (EAN/SSCC reuse) — scans resolve within the trip context.
/// </summary>
public class CargoItem : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }

    public int Sequence { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public decimal ExpectedQuantity { get; set; } = 1;

    public string? QuantityUnit { get; set; }

    public string? Notes { get; set; }
}
