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
/// ReviewRequired + reasons; every other status → NotReady.
/// </summary>
public static class InvoiceReadinessEvaluator
{
    public const string NotReady = "NotReady";
    public const string ReadyForInvoice = "ReadyForInvoice";
    public const string ReviewRequired = "ReviewRequired";

    /// <summary>
    /// Mutates the tracked order; the caller's SaveChanges persists it. <paramref name="tripExecutedOverride"/>
    /// lets the trip-completion flow assert the execution fact while its own trip status is
    /// still unsaved (the query here would read the pre-save status).
    /// </summary>
    public static async Task EvaluateAsync(
        TransportationDbContext db, TransportOrder order, CancellationToken cancellationToken,
        bool? tripExecutedOverride = null)
    {
        if (order.Status != TransportOrderStatus.Completed)
        {
            order.InvoiceReadiness = NotReady;
            order.InvoiceReadinessReasons = null;
            return;
        }

        var reasons = new List<string>();
        var tenantId = order.TenantId;

        var snapshot = await db.TransportOrderPricingSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TransportOrderId == order.Id)
            .Select(s => new { s.CoverageStatus, s.IsStale })
            .FirstOrDefaultAsync(cancellationToken);
        // No snapshot at all: acceptable only when a manual/agreed price exists (pre-engine
        // orders keep invoicing exactly as before) — otherwise there is nothing to invoice.
        if (snapshot is null)
        {
            if (order.AgreedPrice is null)
            {
                reasons.Add("pricing.none");
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
                if (unloadingStopIds.Any(id => !coveredStopIds.Contains(id)))
                {
                    reasons.Add("pod.missing");
                }
            }
        }

        order.InvoiceReadiness = reasons.Count == 0 ? ReadyForInvoice : ReviewRequired;
        order.InvoiceReadinessReasons = reasons.Count == 0 ? null : string.Join(";", reasons);
    }
}
