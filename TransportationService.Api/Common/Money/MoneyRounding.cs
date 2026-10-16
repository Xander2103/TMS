namespace TransportationService.Api.Common.Money;

/// <summary>
/// Transport acceptance T-H4: the ONE rounding rule for euro amounts. The product already rounds
/// commercial amounts half away from zero to cents in the Peppol/UBL invoice, price adjustments and
/// the diesel surcharge; sales lines and invoice line totals used .NET's default banker's rounding,
/// so 2,5 × € 125,33 = € 313,325 became € 313,32 on the line but € 313,33 in the preview and in the
/// UBL — this helper aligns every place that turns a quantity × unit price into money.
/// </summary>
public static class MoneyRounding
{
    public static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>quantity × unit price, exact in decimal, rounded to cents.</summary>
    public static decimal LineTotal(decimal quantity, decimal unitPrice) => Round2(quantity * unitPrice);
}
