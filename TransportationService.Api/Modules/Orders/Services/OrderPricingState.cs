using System.Linq.Expressions;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// The ONE definition of "this order has a sales price" (UX sprint 2026-09-09 §2.5, hardened
/// 2026-09-10), shared by the dossier list, the dossier financials, the per-order flag and the
/// readiness rules.
/// <para>
/// € 0 is a legitimate price in this TMS: a manual override may be 0 (goodwill transport with a
/// reason) and a one-off price agreement may be 0 (the backend accepts <c>OneOffFixedAmount = 0</c>
/// and the engine then derives <c>AgreedPrice = 0</c>). "Priced" therefore follows the pricing
/// PROVENANCE, never the amount alone:
/// </para>
/// <list type="bullet">
///   <item><see cref="TransportOrder.PriceIsManual"/> — an explicit override (orders.override_price), any amount;</item>
///   <item><see cref="OrderPricingSource.OneOff"/> with a fixed amount — an explicit one-off agreement, any amount;</item>
///   <item>a positive <see cref="TransportOrder.AgreedPrice"/> — derived by the engine from counted lines, or entered as the legacy amount.</item>
/// </list>
/// <para>
/// <c>AgreedPrice == 0</c> WITHOUT such provenance is the engine's "nothing counted" zero
/// (only "Geen tarief geconfigureerd" placeholders) and reads as not priced; <c>null</c> means
/// nothing was ever derived or entered. Known limitation (documented, not covered): manual
/// sales lines that net to exactly € 0 without an override or one-off agreement also read as
/// not priced — record such an order through an override.
/// </para>
/// <para>
/// <see cref="IsPricedExpression"/> is the EF-translatable form; the C# overloads are compiled
/// from the same tree so the two cannot drift. Projections that must stay inline (nested
/// <c>Count</c>/<c>Any</c> inside a <c>Select</c>) are covered by <c>OrderPricingParityTests</c>.
/// </para>
/// </summary>
public static class OrderPricingState
{
    /// <summary>EF-translatable predicate (use with <c>Where</c>/<c>Count</c>/<c>Any</c> on TransportOrders).</summary>
    public static readonly Expression<Func<TransportOrder, bool>> IsPricedExpression = o =>
        o.PriceIsManual
        || (o.PricingSource == OrderPricingSource.OneOff && o.OneOffFixedAmount != null)
        || o.AgreedPrice > 0m;

    /// <summary>Negation of <see cref="IsPricedExpression"/>, built from the same tree (C# null semantics preserved by EF).</summary>
    public static readonly Expression<Func<TransportOrder, bool>> IsUnpricedExpression =
        Expression.Lambda<Func<TransportOrder, bool>>(Expression.Not(IsPricedExpression.Body), IsPricedExpression.Parameters);

    private static readonly Func<TransportOrder, bool> Compiled = IsPricedExpression.Compile();

    public static bool IsPriced(TransportOrder order) => Compiled(order);

    /// <summary>Primitive overload for read-model rows that project only these four columns.</summary>
    public static bool IsPriced(bool priceIsManual, OrderPricingSource pricingSource, decimal? oneOffFixedAmount, decimal? agreedPrice) =>
        Compiled(new TransportOrder
        {
            PriceIsManual = priceIsManual,
            PricingSource = pricingSource,
            OneOffFixedAmount = oneOffFixedAmount,
            AgreedPrice = agreedPrice,
        });

    /// <summary>
    /// The effective agreed sales price as the dossier reads it (step 13): <c>AgreedPrice</c>
    /// once the engine derived it, else — for a one-off agreement the engine has not run for yet
    /// (no cargo, no tariff configuration) — the agreed fixed amount itself. Null when nothing
    /// is agreed or derived. In EF projections inline the same tree:
    /// <c>o.AgreedPrice ?? (o.PricingSource == OrderPricingSource.OneOff ? o.OneOffFixedAmount : null)</c>.
    /// </summary>
    public static decimal? EffectiveAgreedPrice(OrderPricingSource pricingSource, decimal? oneOffFixedAmount, decimal? agreedPrice) =>
        agreedPrice ?? (pricingSource == OrderPricingSource.OneOff ? oneOffFixedAmount : null);

    /// <summary>
    /// Intentional € 0 (step 13): the price PROVENANCE itself is zero — a one-off agreement at 0
    /// or a manual override at 0. Never the engine's empty zero (that is unpriced), never a
    /// one-off amount the engine has not derived yet. Same tree for SQL:
    /// <c>(o.PricingSource == OneOff &amp;&amp; o.OneOffFixedAmount == 0m) || (o.PriceIsManual &amp;&amp; o.AgreedPrice == 0m)</c>.
    /// </summary>
    public static bool IsIntentionalZero(bool priceIsManual, OrderPricingSource pricingSource, decimal? oneOffFixedAmount, decimal? agreedPrice) =>
        (pricingSource == OrderPricingSource.OneOff && oneOffFixedAmount == 0m)
        || (priceIsManual && agreedPrice == 0m);
}
