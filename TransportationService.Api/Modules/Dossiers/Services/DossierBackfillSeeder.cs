using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// Wraps every pre-dossier-wave TransportOrder in its own dossier so the dossier list is the
/// single entry point to ALL work, historical included. Runs idempotently on every startup:
/// an order is skipped when it already has an active dossier link (user-created dossiers are
/// never touched) or an existing wrapper. The filtered unique index on
/// <c>OriginTransportOrderId</c> makes duplicate wrappers impossible even under races or
/// partial failures — a re-run simply continues where the previous one stopped.
/// Wrapper CreatedAt is the backfill moment (truthful); the business date is DossierDate,
/// copied from the order. No per-row audit records: the wrapper origin is recognisable from
/// OriginTransportOrderId and would otherwise flood the audit log.
/// </summary>
public static class DossierBackfillSeeder
{
    private const int ChunkSize = 200;

    private static readonly TransportOrderStatus[] ClosedStatuses =
    [
        TransportOrderStatus.Completed, TransportOrderStatus.Invoiced, TransportOrderStatus.Cancelled,
    ];

    public static async Task<int> SyncAsync(
        TransportationDbContext db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var created = 0;
        var tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            created += await BackfillTenantAsync(db, tenantId, logger, cancellationToken);
        }

        return created;
    }

    private static async Task<int> BackfillTenantAsync(
        TransportationDbContext db, Guid tenantId, ILogger? logger, CancellationToken cancellationToken)
    {
        // Orders without an active dossier link and without a wrapper. Runs with the tenant
        // filter open (startup has no tenant context), so every query scopes explicitly.
        var linkedOrderIds = db.DossierOrders
            .Where(l => l.TenantId == tenantId)
            .Select(l => l.TransportOrderId);
        var wrappedOrderIds = db.TransportDossiers.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.OriginTransportOrderId != null)
            .Select(d => d.OriginTransportOrderId!.Value);

        var orders = await db.TransportOrders
            .Where(o => o.TenantId == tenantId
                        && !linkedOrderIds.Contains(o.Id)
                        && !wrappedOrderIds.Contains(o.Id))
            .OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.CustomerId, o.CustomerReference, o.LegalEntityId,
                o.OrderDate, o.Status, o.UpdatedAt, o.CancellationReason,
            })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return 0;
        }

        // The wrapper activity needs the tenant's default transport type; seed the catalogue
        // first (idempotent) and fall back gracefully for tenants that reshaped it.
        await new ActivityTypeSeeder(db, new DevTenantContext(tenantId)).EnsureSeededAsync(cancellationToken);
        var transportType = await db.ActivityTypes
                .Where(t => t.TenantId == tenantId && t.IsActive && t.IsSystemDefaultTransport)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await db.ActivityTypes
                .Where(t => t.TenantId == tenantId && t.IsActive && t.HasStops)
                .OrderBy(t => t.SortOrder)
                .FirstOrDefaultAsync(cancellationToken);
        if (transportType is null)
        {
            logger?.LogWarning(
                "Dossier backfill skipped for tenant {TenantId}: no active transport-capable activity type.", tenantId);
            return 0;
        }

        var customerNames = await db.Customers
            .Where(c => c.TenantId == tenantId)
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var settings = await db.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var created = 0;
        foreach (var chunk in orders.Chunk(ChunkSize))
        {
            var pending = new List<TransportDossier>(chunk.Length);
            // D6: a wrapper becomes the owning dossier of its order, so that order's documents get
            // its DossierId in the same save (only the link column; no file is touched).
            var chunkOrderIds = chunk.Select(o => (Guid?)o.Id).ToList();
            var orphanDocuments = (await db.TransportOrderDocuments
                    .Where(d => d.TenantId == tenantId && d.DossierId == null && chunkOrderIds.Contains(d.TransportOrderId))
                    .ToListAsync(cancellationToken))
                .ToLookup(d => d.TransportOrderId!.Value);
            foreach (var order in chunk)
            {
                var customerName = order.CustomerId is { } customerId
                    ? customerNames.GetValueOrDefault(customerId)
                    : null;
                var title = customerName is null ? order.OrderNumber : $"{order.OrderNumber} — {customerName}";
                var dossier = new TransportDossier
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Title = title.Length > 200 ? title[..200] : title,
                    CustomerId = order.CustomerId,
                    CustomerReference = order.CustomerReference,
                    LegalEntityId = order.LegalEntityId,
                    DossierDate = order.OrderDate,
                    // A wrapper for an order that already reached a terminal status is born CONFIRMED,
                    // and honestly labelled: the pipeline (the order) decided it, nobody clicked.
                    // A Cancelled order yields a CANCELLED wrapper (confirmation sprint 2026-09-23).
                    Status = order.Status == TransportOrderStatus.Cancelled ? DossierStatus.Cancelled
                        : ClosedStatuses.Contains(order.Status) ? DossierStatus.Closed : DossierStatus.Open,
                    ClosedAt = order.Status != TransportOrderStatus.Cancelled && ClosedStatuses.Contains(order.Status) ? order.UpdatedAt : null,
                    ConfirmationSource = order.Status != TransportOrderStatus.Cancelled && ClosedStatuses.Contains(order.Status) ? DossierConfirmationSource.Automatic : null,
                    ConfirmationReason = order.Status != TransportOrderStatus.Cancelled && ClosedStatuses.Contains(order.Status) ? "Backfill: opdracht was al afgerond" : null,
                    CancelledAt = order.Status == TransportOrderStatus.Cancelled ? order.UpdatedAt : null,
                    CancellationReason = order.Status == TransportOrderStatus.Cancelled ? (order.CancellationReason ?? "Backfill: opdracht was geannuleerd") : null,
                    OriginTransportOrderId = order.Id,
                };
                pending.Add(dossier);
                db.TransportDossiers.Add(dossier);
                foreach (var document in orphanDocuments[order.Id])
                {
                    document.DossierId = dossier.Id;
                }

                db.DossierActivities.Add(new DossierActivity
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossier.Id,
                    ActivityTypeId = transportType.Id, Sequence = 1, LinkedTransportOrderId = order.Id,
                });
                db.DossierOrders.Add(new DossierOrder
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossier.Id, TransportOrderId = order.Id,
                });
            }

            // Numbers are (re)claimed inside the delegate so a counter-concurrency retry
            // reassigns fresh values for the whole chunk — duplicates are impossible.
            await TenantNumbering.SaveWithClaimedNumberAsync(db, settings, () =>
            {
                foreach (var dossier in pending)
                {
                    dossier.DossierNumber = settings is null
                        ? $"DOS-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
                        : $"{settings.DossierNumberPrefix}{settings.DossierNumberNextValue++:0000}";
                }
            }, cancellationToken);
            created += pending.Count;
        }

        logger?.LogInformation(
            "Dossier backfill created {Count} wrapper dossiers for tenant {TenantId}.", created, tenantId);
        return created;
    }
}
