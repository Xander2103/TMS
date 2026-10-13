using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Warehousing.Entities;

namespace TransportationService.Api.Modules.Warehousing.Services;

/// <summary>
/// Wave 5 §1: the CLOSE side of the movement-based storage clock. Watches every save for
/// newly staged leave-type custody events (load scans, return/redelivery loads, gone-for-good
/// dispositions) and closes the package's open <see cref="StorageStay"/> in the same save —
/// one central hook instead of edits in every staging site, so future leave paths are covered
/// automatically. Opening a stay stays explicit (WarehouseScanService knows the warehouse).
/// The same hook clears the package's <c>CurrentWarehouseLocationId</c> projection: goods that
/// physically left on a vehicle must never keep showing a warehouse location, and a later
/// failed delivery on the road must not resurrect one — only a real warehouse scan sets it.
/// </summary>
public sealed class StorageClockInterceptor : SaveChangesInterceptor
{
    private static readonly PackageEventType[] LeaveEvents =
    [
        PackageEventType.LoadScan,
        PackageEventType.ReturnLoaded,
        PackageEventType.RedeliveryLoaded,
        PackageEventType.ReturnedToSender,
        PackageEventType.Cancelled,
    ];

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is TransportationDbContext db)
        {
            var leaves = db.ChangeTracker.Entries<PackageEvent>()
                .Where(e => e.State == EntityState.Added && LeaveEvents.Contains(e.Entity.EventType))
                .Select(e => e.Entity)
                .ToList();
            foreach (var leave in leaves)
            {
                // Already tracked open stay (same unit of work) wins; else load it.
                var open = db.ChangeTracker.Entries<StorageStay>()
                        .Select(e => e.Entity)
                        .FirstOrDefault(s => s.TenantId == leave.TenantId && s.PackageId == leave.PackageId && s.OutAt == null)
                    ?? await db.StorageStays
                        .FirstOrDefaultAsync(s => s.TenantId == leave.TenantId && s.PackageId == leave.PackageId
                                                  && s.OutAt == null, cancellationToken);
                if (open is not null && open.OutAt is null)
                {
                    open.OutAt = leave.OccurredAt;
                    open.OutPackageEventId = leave.Id;
                }

                // Physical departure ends the location projection (same unit of work).
                var package = db.ChangeTracker.Entries<Package>()
                        .Select(e => e.Entity)
                        .FirstOrDefault(p => p.TenantId == leave.TenantId && p.Id == leave.PackageId)
                    ?? await db.Packages
                        .FirstOrDefaultAsync(p => p.TenantId == leave.TenantId && p.Id == leave.PackageId, cancellationToken);
                if (package is not null)
                {
                    package.CurrentWarehouseLocationId = null;
                }
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
