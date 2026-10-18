using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Incidents.Entities;

namespace TransportationService.Api.Modules.Incidents.Services;

/// <summary>
/// EX-04: the ONE authoritative answer to "which problems concern this dossier?" — used by
/// dossier confirmation, the dossier's incident list and the incident list filter alike.
/// <list type="bullet">
/// <item>An <b>incident</b> concerns a dossier when it names the dossier directly, or — without a
/// dossier of its own — through its order (order ∈ dossier), or — without order — through its
/// trip (the trip carries an order of the dossier). Forms never have to fill every redundant key.</item>
/// <item>An <b>afwijking</b> has no dossier key: its order decides, else its trip.</item>
/// </list>
/// A dossier's orders are its linked orders (DossierOrders), its activities' orders and its
/// origin order — the same set dossier confirmation evaluates.
/// </summary>
public static class ProblemScope
{
    public static readonly IncidentStatus[] OpenIncidentStatuses = [IncidentStatus.New, IncidentStatus.InProgress];

    public static readonly ExecutionExceptionStatus[] OpenExceptionStatuses =
        [ExecutionExceptionStatus.Open, ExecutionExceptionStatus.Investigating, ExecutionExceptionStatus.CustomerActionRequired];

    public static bool IsOpen(IncidentStatus status) => OpenIncidentStatuses.Contains(status);

    public static bool IsOpen(ExecutionExceptionStatus status) => OpenExceptionStatuses.Contains(status);

    public sealed record DossierScope(Guid DossierId, IReadOnlyList<Guid> OrderIds, IReadOnlyList<Guid> TripIds);

    public static async Task<DossierScope> ForDossierAsync(
        TransportationDbContext db, Guid tenantId, Guid dossierId, CancellationToken cancellationToken)
    {
        var orderIds = (await db.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.DossierId == dossierId)
                .Select(l => l.TransportOrderId)
                .ToListAsync(cancellationToken))
            .Concat(await db.DossierActivities.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.DossierId == dossierId && a.LinkedTransportOrderId != null)
                .Select(a => a.LinkedTransportOrderId!.Value)
                .ToListAsync(cancellationToken))
            .Concat(await db.TransportDossiers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.Id == dossierId && d.OriginTransportOrderId != null)
                .Select(d => d.OriginTransportOrderId!.Value)
                .ToListAsync(cancellationToken))
            .Distinct()
            .ToList();

        var tripIds = orderIds.Count == 0
            ? []
            : await db.TripOrders.AsNoTracking()
                .Where(t => t.TenantId == tenantId && orderIds.Contains(t.TransportOrderId))
                .Select(t => t.TripId)
                .Distinct()
                .ToListAsync(cancellationToken);

        return new DossierScope(dossierId, orderIds, tripIds);
    }

    /// <summary>Incidents of the tenant that concern the dossier (any status — callers filter).</summary>
    public static IQueryable<Incident> Incidents(TransportationDbContext db, Guid tenantId, DossierScope scope)
    {
        var dossierId = scope.DossierId;
        var orderIds = scope.OrderIds.ToList();
        var tripIds = scope.TripIds.ToList();
        return db.Incidents.Where(i => i.TenantId == tenantId
            && (i.DossierId == dossierId
                || (i.DossierId == null && i.TransportOrderId != null && orderIds.Contains(i.TransportOrderId.Value))
                || (i.DossierId == null && i.TransportOrderId == null && i.TripId != null && tripIds.Contains(i.TripId.Value))));
    }

    /// <summary>Afwijkingen of the tenant that concern the dossier (any status — callers filter).</summary>
    public static IQueryable<ExecutionException> Exceptions(TransportationDbContext db, Guid tenantId, DossierScope scope)
    {
        var orderIds = scope.OrderIds.ToList();
        var tripIds = scope.TripIds.ToList();
        return db.ExecutionExceptions.Where(e => e.TenantId == tenantId
            && ((e.TransportOrderId != null && orderIds.Contains(e.TransportOrderId.Value))
                || (e.TransportOrderId == null && tripIds.Contains(e.TripId))));
    }

    /// <summary>The dossier(s) an order belongs to (wrapper/origin first, then links and activities).</summary>
    public static async Task<IReadOnlyList<Guid>> DossiersOfOrderAsync(
        TransportationDbContext db, Guid tenantId, Guid orderId, CancellationToken cancellationToken)
    {
        var origin = await db.TransportDossiers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.OriginTransportOrderId == orderId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        var linked = await db.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == orderId)
            .OrderBy(l => l.CreatedAt)
            .Select(l => l.DossierId)
            .ToListAsync(cancellationToken);
        var activities = await db.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == orderId)
            .Select(a => a.DossierId)
            .ToListAsync(cancellationToken);
        return origin.Concat(linked).Concat(activities).Distinct().ToList();
    }

    /// <summary>Every dossier an incident concerns (direct, else via order, else via trip).</summary>
    public static async Task<IReadOnlyList<Guid>> DossiersOfIncidentAsync(
        TransportationDbContext db, Guid tenantId, Incident incident, CancellationToken cancellationToken)
    {
        if (incident.DossierId is { } direct)
        {
            return [direct];
        }

        if (incident.TransportOrderId is { } orderId)
        {
            return await DossiersOfOrderAsync(db, tenantId, orderId, cancellationToken);
        }

        return incident.TripId is { } tripId
            ? await DossiersOfTripAsync(db, tenantId, tripId, cancellationToken)
            : [];
    }

    /// <summary>Every dossier an afwijking concerns (via its order, else via its trip).</summary>
    public static async Task<IReadOnlyList<Guid>> DossiersOfExceptionAsync(
        TransportationDbContext db, Guid tenantId, ExecutionException exception, CancellationToken cancellationToken) =>
        exception.TransportOrderId is { } orderId
            ? await DossiersOfOrderAsync(db, tenantId, orderId, cancellationToken)
            : await DossiersOfTripAsync(db, tenantId, exception.TripId, cancellationToken);

    private static async Task<IReadOnlyList<Guid>> DossiersOfTripAsync(
        TransportationDbContext db, Guid tenantId, Guid tripId, CancellationToken cancellationToken)
    {
        var orderIds = await db.TripOrders.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.TripId == tripId)
            .Select(t => t.TransportOrderId)
            .ToListAsync(cancellationToken);
        var result = new List<Guid>();
        foreach (var orderId in orderIds)
        {
            result.AddRange(await DossiersOfOrderAsync(db, tenantId, orderId, cancellationToken));
        }

        return result.Distinct().ToList();
    }
}
