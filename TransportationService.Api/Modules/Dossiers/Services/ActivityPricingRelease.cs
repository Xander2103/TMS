using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// The activity-side twin of <see cref="Orders.Services.OrderPricingSnapshotRelease"/>: the ONE
/// rule for taking a standalone activity's price out of the terminal
/// <see cref="OrderPricingStatus.Invoiced"/> state when it stops being invoiced (draft invoice
/// cancelled or deleted, its lines dropped from the draft, or the dossier moved to another
/// customer). Staging only — the caller owns the SaveChanges.
/// </summary>
public static class ActivityPricingRelease
{
    /// <summary>Invoiced → Locked: still frozen, no longer a dead end (invoice cancelled/deleted/edited).</summary>
    public static Task ReleaseAsync(
        TransportationDbContext dbContext, Guid tenantId, IReadOnlyList<Guid> activityIds, CancellationToken cancellationToken) =>
        MoveAsync(dbContext, tenantId, activityIds, OrderPricingStatus.Locked, cancellationToken);

    /// <summary>
    /// Invoiced/Locked → Draft: the agreed price was made under a customer that no longer owns
    /// the dossier, so it must be confirmed anew (customer change).
    /// </summary>
    public static Task ReopenAsync(
        TransportationDbContext dbContext, Guid tenantId, IReadOnlyList<Guid> activityIds, CancellationToken cancellationToken) =>
        MoveAsync(dbContext, tenantId, activityIds, OrderPricingStatus.Draft, cancellationToken);

    private static async Task MoveAsync(
        TransportationDbContext dbContext, Guid tenantId, IReadOnlyList<Guid> activityIds, OrderPricingStatus target,
        CancellationToken cancellationToken)
    {
        if (activityIds.Count == 0)
        {
            return;
        }

        var pricings = await dbContext.DossierActivityPricings
            .Where(p => p.TenantId == tenantId && activityIds.Contains(p.DossierActivityId)
                        && (p.Status == OrderPricingStatus.Invoiced || (target == OrderPricingStatus.Draft && p.Status == OrderPricingStatus.Locked)))
            .ToListAsync(cancellationToken);
        foreach (var pricing in pricings)
        {
            pricing.Status = target;
            pricing.Version = Guid.NewGuid();
        }
    }
}
