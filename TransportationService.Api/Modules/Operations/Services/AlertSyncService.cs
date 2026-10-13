using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Operations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Operations.Services;

/// <summary>
/// Computes the current operational alert conditions from existing data (ETA, POD, incidents,
/// execution exceptions, maintenance, inspections, fleet documents) and reconciles them with
/// the stored <see cref="OperationalAlert"/> rows. No condition data is ever invented; every
/// rule reads real records. Maintenance overdue is date-based here — the odometer-based check
/// stays on the fleet dashboard, which has the vehicle context to explain it.
/// </summary>
public class AlertSyncService : IAlertSyncService
{
    private const int DefaultDocumentWarningDays = 30;
    private const int PodLookbackDays = 3;

    /// <summary>Categories this sync owns; alerts in other categories are never auto-resolved.</summary>
    private static readonly string[] SyncedCategories =
        ["Delay", "Pod", "Incident", "Exception", "Maintenance", "Inspection", "Document"];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AlertSyncService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    private sealed record Candidate(
        string DedupeKey, AlertSeverity Severity, string Category, string Source,
        string Title, string Message, string? LinkPath, string? RelatedEntityType, Guid? RelatedEntityId);

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var candidates = new List<Candidate>();
        await CollectDelayedTripsAsync(candidates, tenantId, cancellationToken);
        await CollectMissingPodAsync(candidates, tenantId, today, cancellationToken);
        await CollectCriticalIncidentsAsync(candidates, tenantId, cancellationToken);
        await CollectOpenExceptionsAsync(candidates, tenantId, cancellationToken);
        await CollectMaintenanceAsync(candidates, tenantId, today, cancellationToken);
        await CollectInspectionsAsync(candidates, tenantId, today, cancellationToken);
        await CollectExpiringDocumentsAsync(candidates, tenantId, today, cancellationToken);

        // Reconcile against every stored alert in the synced categories (bounded: unresolved
        // ones plus any row matching a current candidate key, so re-appearing conditions reuse
        // their old row instead of violating the unique dedupe index).
        var keys = candidates.Select(c => c.DedupeKey).ToList();
        var existing = await _dbContext.OperationalAlerts
            .Where(a => a.TenantId == tenantId && SyncedCategories.Contains(a.Category))
            .Where(a => a.Status != AlertStatus.Resolved || keys.Contains(a.DedupeKey))
            .ToListAsync(cancellationToken);
        var existingByKey = existing.ToDictionary(a => a.DedupeKey);

        foreach (var candidate in candidates)
        {
            if (existingByKey.TryGetValue(candidate.DedupeKey, out var alert))
            {
                alert.LastSeenAt = now;
                alert.Severity = candidate.Severity;
                alert.Message = candidate.Message;
                if (alert.Status == AlertStatus.Resolved)
                {
                    // The condition came back after being resolved: reopen the same row.
                    alert.Status = AlertStatus.Active;
                    alert.ResolvedAt = null;
                    alert.ResolvedByUserId = null;
                    alert.AcknowledgedAt = null;
                    alert.AcknowledgedByUserId = null;
                }
            }
            else
            {
                _dbContext.OperationalAlerts.Add(new OperationalAlert
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Severity = candidate.Severity,
                    Category = candidate.Category,
                    Source = candidate.Source,
                    Title = candidate.Title,
                    Message = candidate.Message,
                    LinkPath = candidate.LinkPath,
                    RelatedEntityType = candidate.RelatedEntityType,
                    RelatedEntityId = candidate.RelatedEntityId,
                    DedupeKey = candidate.DedupeKey,
                    Status = AlertStatus.Active,
                    LastSeenAt = now,
                });
            }
        }

        // Conditions that disappeared resolve automatically (system, no user id).
        var candidateKeys = candidates.Select(c => c.DedupeKey).ToHashSet();
        foreach (var alert in existing.Where(a => a.Status != AlertStatus.Resolved && !candidateKeys.Contains(a.DedupeKey)))
        {
            alert.Status = AlertStatus.Resolved;
            alert.ResolvedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Active trips with at least one stop whose live ETA is Late.</summary>
    private async Task CollectDelayedTripsAsync(
        List<Candidate> candidates, Guid tenantId, CancellationToken cancellationToken)
    {
        var delayed = await _dbContext.StopEtas.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == EtaStatus.Late)
            .Join(_dbContext.Trips.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.Status == TripStatus.InProgress),
                e => e.TripId, t => t.Id,
                (e, t) => new { t.Id, t.TripNumber })
            .Distinct()
            .ToListAsync(cancellationToken);

        candidates.AddRange(delayed.Select(t => new Candidate(
            $"trip-delay:{t.Id}", AlertSeverity.Warning, "Delay", "trip_delayed",
            $"Rit {t.TripNumber} loopt vertraging op",
            $"Minstens één stop van rit {t.TripNumber} heeft een verwachte aankomst na het geplande venster.",
            $"/planning/{t.Id}", "Trip", t.Id)));
    }

    /// <summary>Completed unloading stops (recent trips) without a current POD.</summary>
    private async Task CollectMissingPodAsync(
        List<Candidate> candidates, Guid tenantId, DateOnly today, CancellationToken cancellationToken)
    {
        var since = today.AddDays(-PodLookbackDays);
        var missing = await _dbContext.StopExecutions.AsNoTracking()
            .Where(se => se.TenantId == tenantId
                         && (se.Status == StopExecutionStatus.Completed || se.Status == StopExecutionStatus.PartiallyCompleted))
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == tenantId && t.TripDate >= since),
                se => se.TripId, t => t.Id, (se, t) => new { se, t })
            .Join(_dbContext.TransportOrderStops.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.StopType == StopType.Unloading),
                x => x.se.TransportOrderStopId, s => s.Id, (x, s) => new { x.se, x.t })
            .Where(x => !_dbContext.ProofsOfDelivery.Any(p =>
                p.TenantId == tenantId && p.TransportOrderStopId == x.se.TransportOrderStopId && p.IsCurrent))
            .Select(x => new { StopExecutionId = x.se.Id, x.t.Id, x.t.TripNumber })
            .ToListAsync(cancellationToken);

        candidates.AddRange(missing.Select(m => new Candidate(
            $"missing-pod:{m.StopExecutionId}", AlertSeverity.Warning, "Pod", "pod_missing",
            $"Afleverbewijs ontbreekt op rit {m.TripNumber}",
            $"Een afgeronde losstop van rit {m.TripNumber} heeft geen afleverbewijs.",
            $"/planning/{m.Id}", "Trip", m.Id)));
    }

    private async Task CollectCriticalIncidentsAsync(
        List<Candidate> candidates, Guid tenantId, CancellationToken cancellationToken)
    {
        var incidents = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId
                        && i.Severity == IncidentSeverity.Critical
                        && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress))
            .Select(i => new { i.Id, i.Title })
            .ToListAsync(cancellationToken);

        candidates.AddRange(incidents.Select(i => new Candidate(
            $"incident:{i.Id}", AlertSeverity.Critical, "Incident", "incident_critical",
            "Kritiek incident open",
            $"Incident \"{i.Title}\" heeft ernst Kritiek en is nog niet afgehandeld.",
            $"/incidents/{i.Id}", "Incident", i.Id)));
    }

    /// <summary>Open execution exceptions (scan/package discrepancies, delivery problems).</summary>
    private async Task CollectOpenExceptionsAsync(
        List<Candidate> candidates, Guid tenantId, CancellationToken cancellationToken)
    {
        var exceptions = await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && (e.Status == ExecutionExceptionStatus.Open
                            || e.Status == ExecutionExceptionStatus.Investigating
                            || e.Status == ExecutionExceptionStatus.CustomerActionRequired))
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == tenantId),
                e => e.TripId, t => t.Id,
                (e, t) => new { e.Id, e.Type, e.Severity, t.TripNumber })
            .ToListAsync(cancellationToken);

        candidates.AddRange(exceptions.Select(e => new Candidate(
            $"exception:{e.Id}",
            e.Severity == ExceptionSeverity.Critical ? AlertSeverity.Critical : AlertSeverity.Warning,
            "Exception", "execution_exception",
            $"Afwijking op rit {e.TripNumber}",
            $"Open afwijking ({e.Type}) op rit {e.TripNumber}.",
            "/exceptions", "ExecutionException", e.Id)));
    }

    private async Task CollectMaintenanceAsync(
        List<Candidate> candidates, Guid tenantId, DateOnly today, CancellationToken cancellationToken)
    {
        var overdue = await _dbContext.MaintenanceRecords.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && (m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress)
                        && m.ScheduledDate != null && m.ScheduledDate < today)
            .Select(m => new { m.Id, m.MaintenanceType, m.VehicleId, m.TrailerId })
            .ToListAsync(cancellationToken);

        candidates.AddRange(overdue.Select(m => new Candidate(
            $"maintenance:{m.Id}", AlertSeverity.Warning, "Maintenance", "maintenance_overdue",
            "Onderhoud over tijd",
            $"Gepland onderhoud ({m.MaintenanceType}) is over de geplande datum heen.",
            "/maintenance", m.VehicleId is not null ? "Vehicle" : "Trailer", m.VehicleId ?? m.TrailerId)));
    }

    private async Task CollectInspectionsAsync(
        List<Candidate> candidates, Guid tenantId, DateOnly today, CancellationToken cancellationToken)
    {
        var overdue = await _dbContext.Inspections.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.CompletedDate == null && i.DueDate < today)
            .Select(i => new { i.Id, i.InspectionType, i.VehicleId, i.TrailerId })
            .ToListAsync(cancellationToken);

        candidates.AddRange(overdue.Select(i => new Candidate(
            $"inspection:{i.Id}", AlertSeverity.Warning, "Inspection", "inspection_overdue",
            "Keuring over tijd",
            $"Keuring ({i.InspectionType}) is over de vervaldatum heen.",
            "/inspections", i.VehicleId is not null ? "Vehicle" : "Trailer", i.VehicleId ?? i.TrailerId)));
    }

    private async Task CollectExpiringDocumentsAsync(
        List<Candidate> candidates, Guid tenantId, DateOnly today, CancellationToken cancellationToken)
    {
        // WarningDays varies per document; fetch the near-future set and filter in memory.
        var horizon = today.AddDays(365);
        var documents = await _dbContext.FleetDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ExpiryDate != null && d.ExpiryDate <= horizon)
            .Select(d => new { d.Id, d.DocumentType, d.ExpiryDate, d.WarningDays, d.VehicleId, d.TrailerId })
            .ToListAsync(cancellationToken);

        foreach (var doc in documents)
        {
            var warningDays = doc.WarningDays ?? DefaultDocumentWarningDays;
            if (doc.ExpiryDate is not { } expiry || expiry > today.AddDays(warningDays))
            {
                continue;
            }

            var expired = expiry < today;
            candidates.Add(new Candidate(
                $"fleet-doc:{doc.Id}", AlertSeverity.Warning, "Document", "fleet_document_expiring",
                expired ? "Voertuigdocument verlopen" : "Voertuigdocument verloopt binnenkort",
                expired
                    ? $"Document ({doc.DocumentType}) is verlopen op {expiry:dd-MM-yyyy}."
                    : $"Document ({doc.DocumentType}) verloopt op {expiry:dd-MM-yyyy}.",
                "/fleet-documents", doc.VehicleId is not null ? "Vehicle" : "Trailer", doc.VehicleId ?? doc.TrailerId));
        }
    }
}
