using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Wave 2 §6: deterministic, idempotent projection of one order's invoice readiness onto
/// <c>TransportOrder.InvoiceReadiness</c> (+ semicolon-separated reason codes). Invoked on the
/// existing transition points (order status changes, pricing snapshot changes, POD finalize) —
/// it recomputes from the CURRENT state, so calling it twice is always safe. It fires no
/// notifications (Wave 10 decides what to do with the projection).
///
/// Rules: Completed && coverage Full && !stale && (a current POD per unloading stop when the
/// order ran on a completed trip) → ReadyForInvoice; Completed && anything missing →
/// ReviewRequired + reasons; every other status → NotReady. R-N2: a stop that failed and whose
/// goods were delivered by a completed redelivery needs no POD of its own; the redelivery holds it.
/// </summary>
public static class InvoiceReadinessEvaluator
{
    public const string NotReady = "NotReady";
    public const string ReadyForInvoice = "ReadyForInvoice";
    public const string ReviewRequired = "ReviewRequired";

    /// <summary>The PO reason: the only one the INVOICE itself can resolve (a PO typed on the draft).</summary>
    public const string PoMissing = "po.missing";

    /// <summary>
    /// ACC-B1 + MD-H10: may this order be put on an invoice? Ready, explicitly free, a never-evaluated
    /// but priced order — or an order whose ONLY open point is the missing PO: that one is resolved
    /// on the invoice (registry PO or a PO typed on the draft) and enforced again at issue.
    /// </summary>
    public static bool IsBillable(string readiness, string? reasons, bool free, bool priced) =>
        readiness == ReadyForInvoice
        || free
        || (readiness == NotReady && priced)
        || (readiness == ReviewRequired && reasons == PoMissing);

    /// <summary>
    /// MD-H10: re-evaluates every completed (not yet invoiced) order of a customer and saves. The ONE
    /// refresh behind every change of the customer's PO situation — the PO registry (add / edit /
    /// delete), the billing panel's PO policy and the customer form's "PO verplicht" toggle — so
    /// "Nakijken: PO ontbreekt" appears / disappears without a manual price unlock / relock.
    /// </summary>
    public static async Task RefreshCustomerAsync(
        TransportationDbContext db, Guid tenantId, Guid customerId, CancellationToken cancellationToken)
    {
        var orders = await db.TransportOrders
            .Where(o => o.TenantId == tenantId && o.CustomerId == customerId && o.Status == TransportOrderStatus.Completed)
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return;
        }

        foreach (var order in orders)
        {
            await EvaluateAsync(db, order, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Mutates the tracked order; the caller's SaveChanges persists it. <paramref name="tripExecutedOverride"/>
    /// lets the trip-completion flow assert the execution fact while its own trip status is
    /// still unsaved (the query here would read the pre-save status).
    /// </summary>
    public static async Task EvaluateAsync(
        TransportationDbContext db, TransportOrder order, CancellationToken cancellationToken,
        bool? tripExecutedOverride = null)
    {
        await EvaluateCoreAsync(db, order, tripExecutedOverride, redeliveredOverride: null, cancellationToken);

        // R-N2: finishing a redelivery can settle the proof question of the failed original. Its
        // own new status may still be unsaved here, so that fact is passed along explicitly.
        if (order.RedeliveryOfOrderId is { } originalId)
        {
            var original = await db.TransportOrders.FirstOrDefaultAsync(
                o => o.TenantId == order.TenantId && o.Id == originalId, cancellationToken);
            if (original is { Status: TransportOrderStatus.Completed })
            {
                await EvaluateCoreAsync(db, original, tripExecutedOverride: null,
                    redeliveredOverride: order.Status is TransportOrderStatus.Completed or TransportOrderStatus.Invoiced ? true : null,
                    cancellationToken);
            }
        }
    }

    private static async Task EvaluateCoreAsync(
        TransportationDbContext db, TransportOrder order, bool? tripExecutedOverride, bool? redeliveredOverride,
        CancellationToken cancellationToken)
    {
        if (order.Status != TransportOrderStatus.Completed)
        {
            order.InvoiceReadiness = NotReady;
            order.InvoiceReadinessReasons = null;
            return;
        }

        var reasons = new List<string>();
        var tenantId = order.TenantId;

        // ACC-M1: an explicitly free order is commercially RESOLVED (€ 0 by decision) — no pricing
        // reason applies. A zero that was never confirmed as free is not.
        var explicitlyFree = order.FreeConfirmed
                             || OrderPricingState.IsIntentionalZero(order.PriceIsManual, order.PricingSource, order.OneOffFixedAmount, order.AgreedPrice);
        var snapshot = await db.TransportOrderPricingSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TransportOrderId == order.Id)
            .Select(s => new { s.CoverageStatus, s.IsStale })
            .FirstOrDefaultAsync(cancellationToken);
        if (explicitlyFree)
        {
            // Nothing to price; only staleness could still matter and does not for a free order.
        }
        // No snapshot at all: acceptable only when a manual/agreed price exists (pre-engine
        // orders keep invoicing exactly as before) — otherwise there is nothing to invoice.
        else if (snapshot is null)
        {
            if (order.AgreedPrice is null)
            {
                reasons.Add("pricing.none");
            }
            else if (order.AgreedPrice == 0m)
            {
                reasons.Add("pricing.zero_unconfirmed");
            }
        }
        else
        {
            if (snapshot.CoverageStatus is "Partial" or "None")
            {
                reasons.Add($"pricing.coverage.{snapshot.CoverageStatus!.ToLowerInvariant()}");
            }

            if (snapshot.IsStale)
            {
                reasons.Add("pricing.stale");
            }

            if (order.AgreedPrice == 0m && reasons.Count == 0)
            {
                reasons.Add("pricing.zero_unconfirmed");
            }
        }

        // ACC-M4: a customer that requires a PO is not invoice-ready without one — the control
        // workspace says so BEFORE an invoice is drafted, the issue guard enforces it again.
        var customerPolicy = await db.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == order.CustomerId)
            .Select(c => new { c.PurchaseOrderPolicy })
            .FirstOrDefaultAsync(cancellationToken);
        if (customerPolicy?.PurchaseOrderPolicy == Modules.Partners.Entities.PurchaseOrderPolicy.Required
            && !await db.CustomerPurchaseOrderNumbers.AsNoTracking().AnyAsync(
                po => po.TenantId == tenantId && po.CustomerId == order.CustomerId
                      && po.ValidFrom <= order.OrderDate
                      && (po.ValidUntil == null || po.ValidUntil >= order.OrderDate), cancellationToken))
        {
            reasons.Add(PoMissing);
        }

        // POD requirement only when the order actually ran on a completed trip; a directly
        // completed order (no trip execution) never demands one.
        var ranOnCompletedTrip = tripExecutedOverride
            ?? await db.Trips.AsNoTracking()
                .AnyAsync(t => t.TenantId == tenantId && t.Status == Modules.Planning.Entities.TripStatus.Completed
                               && t.Orders.Any(o => o.TransportOrderId == order.Id), cancellationToken);
        if (ranOnCompletedTrip)
        {
            var unloadingStopIds = await db.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.TransportOrderId == order.Id
                            && s.StopType == StopType.Unloading)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            if (unloadingStopIds.Count > 0)
            {
                var coveredStopIds = await db.ProofsOfDelivery.AsNoTracking()
                    .Where(p => p.TenantId == tenantId && p.TransportOrderId == order.Id && p.IsCurrent)
                    .Select(p => p.TransportOrderStopId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                var uncoveredStopIds = unloadingStopIds.Where(id => !coveredStopIds.Contains(id)).ToList();
                if (uncoveredStopIds.Count > 0
                    && await AnyStopWithoutProofAsync(db, order, uncoveredStopIds, redeliveredOverride, cancellationToken))
                {
                    reasons.Add("pod.missing");
                }
            }
        }

        order.InvoiceReadiness = reasons.Count == 0 ? ReadyForInvoice : ReviewRequired;
        order.InvoiceReadinessReasons = reasons.Count == 0 ? null : string.Join(";", reasons);
    }

    /// <summary>
    /// A stop without a POD still needs one, unless nothing could be signed there: its latest
    /// execution failed (same facts as <see cref="DeliveryOutcomeRules"/>) AND a completed
    /// redelivery delivered the goods. Without that redelivery a failed stop stays "Nakijken".
    /// </summary>
    private static async Task<bool> AnyStopWithoutProofAsync(
        TransportationDbContext db, TransportOrder order, IReadOnlyCollection<Guid> stopIds, bool? redeliveredOverride,
        CancellationToken cancellationToken)
    {
        var redelivered = redeliveredOverride
            ?? await db.TransportOrders.AsNoTracking().AnyAsync(
                o => o.TenantId == order.TenantId && o.RedeliveryOfOrderId == order.Id
                     && (o.Status == TransportOrderStatus.Completed || o.Status == TransportOrderStatus.Invoiced),
                cancellationToken);
        if (!redelivered)
        {
            return true;
        }

        var executions = await db.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == order.TenantId && stopIds.Contains(e.TransportOrderStopId))
            .Select(e => new { e.TransportOrderStopId, e.Status, At = e.CompletedAt ?? e.UpdatedAt })
            .ToListAsync(cancellationToken);
        return stopIds.Any(stopId =>
            executions.Where(e => e.TransportOrderStopId == stopId).MaxBy(e => e.At)?.Status
                is not (Modules.Planning.Entities.StopExecutionStatus.Failed or Modules.Planning.Entities.StopExecutionStatus.Skipped));
    }
}
