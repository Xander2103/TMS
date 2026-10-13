using System.Linq.Expressions;
using TransportationService.Api.Modules.Dossiers.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// The ONE definition of "this standalone activity has a sales price" — the activity-side
/// sibling of <c>OrderPricingState</c> with the same provenance rule: priced means an explicit
/// agreement exists, never "amount &gt; 0". Two provenances (master sprint 2026-09-21 D5):
/// <list type="bullet">
///   <item><see cref="ActivityPricingSource.OneOff"/> with an amount — a fixed agreed price, € 0 included;</item>
///   <item><see cref="ActivityPricingSource.Lines"/> with a total — the sum of the activity's sales
///   lines, or (no lines) an explicitly confirmed free activity. The service only ever stores a
///   Lines total when at least one line exists or <see cref="DossierActivityPricing.FreeConfirmed"/>
///   is set; an emptied line list goes back to <see cref="ActivityPricingSource.None"/>.</item>
/// </list>
/// No record, or <see cref="ActivityPricingSource.None"/>, is unpriced. € 0 by provenance is
/// priced; it gets the non-blocking <c>pricing.zero</c> warning UNLESS it was confirmed free
/// (<see cref="IsUnconfirmedZeroExpression"/>).
/// The expressions are EF-translatable; the C# overloads compile from the same trees.
/// </summary>
public static class ActivityPricingState
{
    public static readonly Expression<Func<DossierActivityPricing, bool>> IsPricedExpression = p =>
        (p.PricingSource == ActivityPricingSource.OneOff && p.FixedAmount != null)
        || (p.PricingSource == ActivityPricingSource.Lines && p.AgreedPrice != null);

    /// <summary>
    /// Priced at exactly € 0 WITHOUT the explicit "free" confirmation — the units that still earn
    /// the <c>pricing.zero</c> warning and the ⚠ marker. Same tree as <see cref="IsPricedExpression"/>
    /// plus the zero/confirmation test (kept literal so EF translates it in nested projections).
    /// </summary>
    public static readonly Expression<Func<DossierActivityPricing, bool>> IsUnconfirmedZeroExpression = p =>
        ((p.PricingSource == ActivityPricingSource.OneOff && p.FixedAmount != null)
         || (p.PricingSource == ActivityPricingSource.Lines && p.AgreedPrice != null))
        && p.AgreedPrice == 0m
        && !p.FreeConfirmed;

    private static readonly Func<DossierActivityPricing, bool> Compiled = IsPricedExpression.Compile();
    private static readonly Func<DossierActivityPricing, bool> CompiledUnconfirmedZero = IsUnconfirmedZeroExpression.Compile();

    public static bool IsPriced(DossierActivityPricing pricing) => Compiled(pricing);

    /// <summary>Primitive overload for read-model rows that project only the provenance columns.</summary>
    public static bool IsPriced(ActivityPricingSource source, decimal? fixedAmount, decimal? agreedPrice) =>
        Compiled(new DossierActivityPricing { PricingSource = source, FixedAmount = fixedAmount, AgreedPrice = agreedPrice });

    public static bool IsUnconfirmedZero(DossierActivityPricing pricing) => CompiledUnconfirmedZero(pricing);

    /// <summary>Primitive overload for read-model rows that project only the provenance columns.</summary>
    public static bool IsUnconfirmedZero(ActivityPricingSource source, decimal? fixedAmount, decimal? agreedPrice, bool freeConfirmed) =>
        CompiledUnconfirmedZero(new DossierActivityPricing
        {
            PricingSource = source, FixedAmount = fixedAmount, AgreedPrice = agreedPrice, FreeConfirmed = freeConfirmed,
        });

    /// <summary>Explicitly free: priced by provenance, total exactly € 0, and confirmed as free.</summary>
    public static bool IsFree(DossierActivityPricing pricing) =>
        Compiled(pricing) && pricing.AgreedPrice == 0m && pricing.FreeConfirmed;
}
