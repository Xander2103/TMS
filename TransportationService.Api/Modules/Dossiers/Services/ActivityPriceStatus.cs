using TransportationService.Api.Modules.Dossiers.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// D5 (master sprint 2026-09-21): the derived commercial status of one activity —
/// <c>NotPriced</c> | <c>PartiallyPriced</c> | <c>Priced</c> | <c>ZeroUnconfirmed</c> | <c>Free</c>,
/// or null for a non-billable activity type (no commercial status at all). Never stored, and never
/// a second definition of "priced": it only combines <see cref="ActivityPricingState"/> /
/// <c>OrderPricingState</c> (provenance, never magnitude) with the order snapshot's coverage.
/// A missing price is <c>NotPriced</c> — never a 0 that would read as <c>Free</c>.
/// <para>
/// Pricing UX sprint 2026-10-08: a standalone € 0 that nobody confirmed is its own state,
/// <see cref="ZeroUnconfirmed"/> ("Controle vereist"), so the UI never shows "€ 0 + Geprijsd +
/// warning" at the same time. Only an explicit confirmation turns it into <see cref="Free"/>.
/// </para>
/// </summary>
public static class ActivityPriceStatus
{
    public const string NotPriced = "NotPriced";
    public const string PartiallyPriced = "PartiallyPriced";
    public const string Priced = "Priced";
    /// <summary>Priced at exactly € 0 WITHOUT the explicit "free" confirmation: still needs a decision.</summary>
    public const string ZeroUnconfirmed = "ZeroUnconfirmed";
    public const string Free = "Free";

    /// <summary>Standalone activity: its own price record decides (null record = not priced).</summary>
    public static string? ForStandalone(bool isBillable, DossierActivityPricing? pricing)
    {
        if (!isBillable)
        {
            return null;
        }

        if (pricing is null || !ActivityPricingState.IsPriced(pricing))
        {
            return NotPriced;
        }

        if (ActivityPricingState.IsFree(pricing))
        {
            return Free;
        }

        return ActivityPricingState.IsUnconfirmedZero(pricing) ? ZeroUnconfirmed : Priced;
    }

    /// <summary>
    /// Order-backed activity: the ORDER decides. <paramref name="hasOrder"/> false (the order does
    /// not exist yet) is simply not priced. An intentional € 0 (one-off/override at 0) is
    /// <c>Free</c> — the one-off price command IS the planner's explicit decision, an order has no
    /// separate confirmation; priced with a snapshot whose coverage is Partial/None, or that went
    /// stale, is <c>PartiallyPriced</c> — the same facts that raise pricing.incomplete / pricing.stale.
    /// </summary>
    public static string? ForOrder(
        bool isBillable, bool hasOrder, bool isPriced, bool isIntentionalZero, string? coverageStatus, bool isStale)
    {
        if (!isBillable)
        {
            return null;
        }

        if (!hasOrder || !isPriced)
        {
            return NotPriced;
        }

        if (isIntentionalZero)
        {
            return Free;
        }

        return coverageStatus is "Partial" or "None" || isStale ? PartiallyPriced : Priced;
    }
}
