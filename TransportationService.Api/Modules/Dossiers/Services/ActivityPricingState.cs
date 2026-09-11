using System.Linq.Expressions;
using TransportationService.Api.Modules.Dossiers.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// The ONE definition of "this standalone activity has a sales price" — the activity-side
/// sibling of <c>OrderPricingState</c> with the same provenance rule: priced means an explicit
/// agreement exists (<see cref="ActivityPricingSource.OneOff"/> with an amount), never "amount
/// &gt; 0". € 0 with an agreement is priced (and gets the non-blocking <c>pricing.zero</c>
/// warning); no record, or <see cref="ActivityPricingSource.None"/>, is unpriced.
/// The expression is EF-translatable; the C# overloads compile from the same tree.
/// </summary>
public static class ActivityPricingState
{
    public static readonly Expression<Func<DossierActivityPricing, bool>> IsPricedExpression = p =>
        p.PricingSource == ActivityPricingSource.OneOff && p.FixedAmount != null;

    private static readonly Func<DossierActivityPricing, bool> Compiled = IsPricedExpression.Compile();

    public static bool IsPriced(DossierActivityPricing pricing) => Compiled(pricing);

    /// <summary>Primitive overload for read-model rows that project only the two provenance columns.</summary>
    public static bool IsPriced(ActivityPricingSource source, decimal? fixedAmount) =>
        Compiled(new DossierActivityPricing { PricingSource = source, FixedAmount = fixedAmount });
}
