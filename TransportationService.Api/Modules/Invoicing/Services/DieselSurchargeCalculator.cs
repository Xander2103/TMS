using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// Pure diesel-surcharge strategy: percentage over a documented basis. Documented
/// variables: base amount (order/invoice freight amount), percentage (customer config or
/// audited order override). Deliberately NOT a formula engine — new strategies extend this
/// class behind the same seam. Amounts are rounded per the configured rule and produced as
/// separate invoice lines, so base lines are never inflated (no double counting).
/// </summary>
public static class DieselSurchargeCalculator
{
    /// <summary>Base amount of one order on the invoice + its optional override percentage.</summary>
    public sealed record OrderBase(Guid OrderId, string OrderNumber, decimal BaseAmount, decimal? OverridePercent);

    public sealed record SurchargeLine(Guid? OrderId, string Description, decimal Amount);

    public static bool Applies(CustomerDieselSurcharge? config, DateOnly invoiceDate) =>
        config is { Enabled: true }
        && (config.EffectiveFrom is null || config.EffectiveFrom <= invoiceDate)
        && (config.EffectiveUntil is null || config.EffectiveUntil >= invoiceDate);

    public static IReadOnlyList<SurchargeLine> BuildLines(
        CustomerDieselSurcharge config, IReadOnlyList<OrderBase> orders, DateOnly invoiceDate)
    {
        if (!Applies(config, invoiceDate) || orders.Count == 0)
        {
            return [];
        }

        if (config.Basis == DieselSurchargeBasis.InvoiceSubtotal)
        {
            // Subtotal basis is inherently aggregated; order overrides do not apply here.
            var subtotal = orders.Sum(o => o.BaseAmount);
            var amount = Round(subtotal * config.Percent / 100m, config.Rounding);
            return amount == 0m
                ? []
                : [new SurchargeLine(null, $"Dieseltoeslag {Pct(config.Percent)}% op factuursubtotaal", amount)];
        }

        var perOrder = orders
            .Select(o =>
            {
                var pct = o.OverridePercent ?? config.Percent;
                return (Order: o, Pct: pct, Amount: Round(o.BaseAmount * pct / 100m, config.Rounding));
            })
            .Where(x => x.Amount != 0m)
            .ToList();
        if (perOrder.Count == 0)
        {
            return [];
        }

        if (config.Presentation == DieselSurchargePresentation.PerOrderLine)
        {
            return perOrder
                .Select(x => new SurchargeLine(
                    x.Order.OrderId,
                    $"Dieseltoeslag {Pct(x.Pct)}% — {x.Order.OrderNumber}",
                    x.Amount))
                .ToList();
        }

        // Aggregated: per-order amounts (overrides respected) summed into one line.
        var distinctPcts = perOrder.Select(x => x.Pct).Distinct().ToList();
        var label = distinctPcts.Count == 1 ? $"Dieseltoeslag {Pct(distinctPcts[0])}%" : "Dieseltoeslag";
        return [new SurchargeLine(null, label, perOrder.Sum(x => x.Amount))];
    }

    private static decimal Round(decimal value, DieselSurchargeRounding rounding) => rounding switch
    {
        DieselSurchargeRounding.RoundUpCent => Math.Ceiling(value * 100m) / 100m,
        _ => Math.Round(value, 2, MidpointRounding.AwayFromZero),
    };

    private static string Pct(decimal value) =>
        value == Math.Truncate(value) ? ((int)value).ToString() : value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("nl-BE"));
}
