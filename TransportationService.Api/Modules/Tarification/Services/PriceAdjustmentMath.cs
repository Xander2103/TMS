using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Tarification.Services;

/// <summary>
/// Shared monetary adjustment math used by scheduled price adjustments (customer/agreement scope)
/// and agreement duplication-with-adjustment: percent multiplies, amountDelta adds a fixed signed
/// amount (never resurrecting a null value); an optional rounding step is applied afterwards
/// (round-half-away-from-zero to the nearest step), then the result is rounded to 2 decimals.
/// A negative result is refused — it would mean a rule can no longer be priced.
/// </summary>
public static class PriceAdjustmentMath
{
    public static decimal? Adjust(decimal? value, decimal? percent, decimal? amountDelta, decimal? roundingStep, string ruleName)
    {
        if (value is not { } v)
        {
            return null;
        }

        var result = percent is { } p ? v * (1 + p / 100m) : v + amountDelta!.Value;
        if (roundingStep is { } step && step > 0)
        {
            result = Math.Round(result / step, MidpointRounding.AwayFromZero) * step;
        }

        result = decimal.Round(result, 2, MidpointRounding.AwayFromZero);
        if (result < 0)
        {
            throw new DomainValidationException("amountDelta", $"Aanpassing zou een negatief tarief opleveren voor '{ruleName}'.");
        }

        return result;
    }
}
