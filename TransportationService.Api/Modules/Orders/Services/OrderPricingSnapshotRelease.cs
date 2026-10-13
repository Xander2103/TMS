using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// The single rule for giving an order's price a way out of the terminal <see
/// cref="OrderPricingStatus.Invoiced"/> state (spec ch. 24-26).
///
/// <c>Invoiced</c> has no outgoing transition and every pricing guard refuses it, so whenever an
/// order stops being invoiced — the invoice is cancelled or deleted, its lines are released
/// because the order moved to another customer or invoicing entity — its snapshot must follow, or
/// the order can never be repriced again and can only be re-invoiced at the stale amount.
///
/// It lives here rather than inside <c>InvoiceService</c> because BOTH sides release orders: the
/// invoice side (<c>InvoiceService.ReleaseOrdersAsync</c>) and the order side
/// (<c>TransportOrderService.ChangeLegalEntityAsync</c> / its dossier twin). Wave 1 fix A (A6):
/// the order side used to skip the snapshot entirely, which is exactly the stranded state the
/// audit's H-06 describes. One rule, two callers, no copy.
///
/// Staging only — the caller owns the SaveChanges.
/// </summary>
public static class OrderPricingSnapshotRelease
{
    /// <summary>
    /// Moves the <c>Invoiced</c> snapshots of these orders back to <c>Locked</c>: the price is
    /// still frozen and still needs the lock permission to change, but it is no longer a dead end.
    /// No-op for orders without a snapshot or whose snapshot was never invoiced.
    /// </summary>
    public static async Task ReleaseAsync(
        TransportationDbContext dbContext, Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return;
        }

        var snapshots = await dbContext.TransportOrderPricingSnapshots
            .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId)
                        && s.Status == OrderPricingStatus.Invoiced)
            .ToListAsync(cancellationToken);
        foreach (var snapshot in snapshots)
        {
            snapshot.Status = OrderPricingStatus.Locked;
        }
    }
}
