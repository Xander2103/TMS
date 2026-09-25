namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// D4 (master sprint 2026-09-21): wire values of <c>CargoItemDto.CommercialCoverage</c> — derived
/// on read in <c>TransportOrderService</c>, never stored.
/// </summary>
public static class CargoCommercialCoverage
{
    /// <summary>At least one existing, non-informational sales line is linked to this goods line.</summary>
    public const string SeparatelyPriced = "SeparatelyPriced";

    /// <summary>The order is priced and this goods line is inside that price (fixed price, or unit coverage "Full").</summary>
    public const string Included = "Included";

    /// <summary>Neither — someone has to look at it.</summary>
    public const string ToReview = "ToReview";
}
