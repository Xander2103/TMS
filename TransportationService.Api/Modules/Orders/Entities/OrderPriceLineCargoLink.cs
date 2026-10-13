using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

/// <summary>
/// D4 (master sprint 2026-09-21): "this sales line prices that goods line". Keyed on the line's
/// stable <see cref="LineKey"/> — never its row id — because Auto/Proposed pricing lines are
/// rewritten wholesale on every recalculation while their LineKey survives. Purely a CONTROL
/// relation (it derives <c>CargoItemDto.CommercialCoverage</c>): it never produces an invoice
/// line and never changes an amount. A plain link row: hard-deleted, no soft delete.
/// </summary>
public class OrderPriceLineCargoLink : ITenantOwned, IHasId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TransportOrderId { get; set; }

    /// <summary><see cref="TransportOrderPricingLine.LineKey"/> of the sales line.</summary>
    public string LineKey { get; set; } = string.Empty;

    public Guid CargoItemId { get; set; }
}
