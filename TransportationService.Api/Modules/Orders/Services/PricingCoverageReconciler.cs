using TransportationService.Api.Modules.Orders.Dtos;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Closure sprint 2026-09-23 (P1) — the ONE rule that turns the engine's frozen per-unit
/// coverage into the EFFECTIVE coverage the warning readers work from (frontend strip, confirm
/// gate, invoice/dossier readiness, activity price status):
/// <list type="bullet">
///   <item>a fixed price (manual override or one-off amount) includes every goods line;</item>
///   <item>an entry whose goods lines are ALL priced through their own sales line (D4 link) is
///   covered — "afzonderlijk geprijsd";</item>
///   <item>everything else keeps what the engine said.</item>
/// </list>
/// The engine verdict is kept on the entry (<c>EngineStatus</c>/<c>EngineReason</c>) so the
/// reconciliation is re-computable: dropping the sales line brings the engine's "None" back.
/// Pure; the caller persists the result.
/// </summary>
public static class PricingCoverageReconciler
{
    public const string CoveredBySeparateLine = "SeparatelyPriced";
    public const string CoveredByFixedPrice = "FixedPrice";
    public const string SeparatelyPricedReason = "Afzonderlijk geprijsd via een eigen verkooplijn";
    public const string FixedPriceReason = "Inbegrepen in de vaste prijs";

    public static (List<OrderPricingCoverageDto> Entries, string Status) Reconcile(
        IReadOnlyList<OrderPricingCoverageDto> entries, IReadOnlySet<Guid> separatelyPricedCargoIds, bool hasFixedPrice)
    {
        var effective = entries.Select(entry =>
        {
            var engineStatus = entry.EngineStatus ?? entry.Status;
            var engineReason = entry.EngineStatus is null ? entry.Reason : entry.EngineReason;

            var coveredBy = engineStatus == "Full"
                ? null
                : hasFixedPrice
                    ? CoveredByFixedPrice
                    : entry.CargoItemIds is { Count: > 0 } ids && ids.All(separatelyPricedCargoIds.Contains)
                        ? CoveredBySeparateLine
                        : null;

            return entry with
            {
                Status = coveredBy is null ? engineStatus : "Full",
                Reason = coveredBy switch
                {
                    CoveredByFixedPrice => FixedPriceReason,
                    CoveredBySeparateLine => SeparatelyPricedReason,
                    _ => engineReason,
                },
                EngineStatus = engineStatus,
                EngineReason = engineReason,
                CoveredBy = coveredBy,
            };
        }).ToList();

        return (effective, StatusOf(effective));
    }

    /// <summary>Worst entry wins: None &gt; Partial &gt; Full; no entries = NotApplicable.</summary>
    public static string StatusOf(IReadOnlyList<OrderPricingCoverageDto> entries) =>
        entries.Count == 0 ? "NotApplicable"
        : entries.Any(c => c.Status == "None") ? "None"
        : entries.Any(c => c.Status == "Partial") ? "Partial"
        : "Full";
}
