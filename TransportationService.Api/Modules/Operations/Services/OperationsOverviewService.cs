using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Operations.Dtos;
using TransportationService.Api.Modules.Operations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Operations.Services;

public interface IOperationsOverviewService
{
    /// <summary>Live control-center projection: today's Planned trips plus everything InProgress.</summary>
    Task<OperationsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Batched projection over existing execution data (stop executions, ETA rows, scans,
/// exceptions, POD). Positions follow the honest source ladder — scan GPS, completed-stop
/// location, planned next stop — and are otherwise Unavailable; nothing is fabricated.
/// </summary>
public class OperationsOverviewService : IOperationsOverviewService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public OperationsOverviewService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<OperationsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var trips = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .Where(t => t.TenantId == tenantId
                        && (t.Status == TripStatus.InProgress
                            || (t.Status == TripStatus.Planned && t.TripDate == today)))
            .OrderBy(t => t.TripDate).ThenBy(t => t.PlannedStart).ThenBy(t => t.TripNumber)
            .Take(200)
            .ToListAsync(cancellationToken);

        var tripIds = trips.Select(t => t.Id).ToList();
        var orderIds = trips.SelectMany(t => t.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId))
            .Distinct().ToList();

        // Batched context (one query per concern; no per-trip roundtrips).
        var driverNames = await ResolveDriverNamesAsync(trips, cancellationToken);
        var vehicleNumbers = await ResolveAssetNumbersAsync(
            trips.Where(t => t.VehicleId != null).Select(t => t.VehicleId!.Value), isVehicle: true, cancellationToken);
        var trailerNumbers = await ResolveAssetNumbersAsync(
            trips.Where(t => t.TrailerId != null).Select(t => t.TrailerId!.Value), isVehicle: false, cancellationToken);

        var stops = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
                .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == tenantId),
                    s => s.LocationId, l => l.Id,
                    (s, locations) => new { Stop = s, Locations = locations })
                .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new StopRow(
                    x.Stop.Id, x.Stop.TransportOrderId, x.Stop.Sequence, x.Stop.StopType,
                    x.Stop.City ?? (l != null ? l.City : null),
                    x.Stop.LocationName ?? (l != null ? l.Name : null),
                    x.Stop.PlannedFrom ?? x.Stop.RequestedFrom,
                    x.Stop.PlannedTo ?? x.Stop.RequestedTo,
                    l != null ? l.Latitude : null,
                    l != null ? l.Longitude : null))
                .ToListAsync(cancellationToken);
        var stopsByOrder = stops.ToLookup(s => s.TransportOrderId);

        var executions = tripIds.Count == 0
            ? []
            : await _dbContext.StopExecutions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId))
                .Select(e => new ExecRow(e.TripId, e.TransportOrderStopId, e.Status))
                .ToListAsync(cancellationToken);
        var executionsByTrip = executions.ToLookup(e => e.TripId);

        var etas = tripIds.Count == 0
            ? []
            : await _dbContext.StopEtas.AsNoTracking()
                .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId))
                .Select(e => new { e.TripId, e.TransportOrderStopId, e.CurrentEta, e.Source, e.Status })
                .ToListAsync(cancellationToken);
        var etasByTrip = etas.ToLookup(e => e.TripId);

        var lastScans = tripIds.Count == 0
            ? []
            : await _dbContext.ScanEvents.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.TripId != null && tripIds.Contains(s.TripId.Value))
                .GroupBy(s => s.TripId!.Value)
                .Select(g => g.OrderByDescending(s => s.OccurredAt).First())
                .ToListAsync(cancellationToken);
        var lastScanByTrip = lastScans.ToDictionary(s => s.TripId!.Value);

        var openExceptions = tripIds.Count == 0
            ? []
            : await _dbContext.ExecutionExceptions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId)
                            && (e.Status == ExecutionExceptionStatus.Open
                                || e.Status == ExecutionExceptionStatus.Investigating
                                || e.Status == ExecutionExceptionStatus.CustomerActionRequired))
                .GroupBy(e => e.TripId)
                .Select(g => new { TripId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.TripId, g => g.Count, cancellationToken);

        var currentPodStops = tripIds.Count == 0
            ? []
            : await _dbContext.ProofsOfDelivery.AsNoTracking()
                .Where(p => p.TenantId == tenantId && tripIds.Contains(p.TripId) && p.IsCurrent)
                .Select(p => new { p.TripId, p.TransportOrderStopId })
                .ToListAsync(cancellationToken);
        var podStopsByTrip = currentPodStops.ToLookup(p => p.TripId, p => p.TransportOrderStopId);

        // Latest GPS fix per trip from custody events (scan-time coordinates).
        var gpsEvents = tripIds.Count == 0
            ? []
            : await _dbContext.PackageEvents.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.TripId != null && tripIds.Contains(e.TripId.Value)
                            && e.Latitude != null && e.Longitude != null)
                .GroupBy(e => e.TripId!.Value)
                .Select(g => g.OrderByDescending(e => e.OccurredAt).First())
                .ToListAsync(cancellationToken);
        var gpsByTrip = gpsEvents.ToDictionary(e => e.TripId!.Value);

        var items = new List<OperationsTripDto>(trips.Count);
        foreach (var trip in trips)
        {
            var tripOrderIds = trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence)
                .Select(o => o.TransportOrderId).ToList();
            var tripStops = tripOrderIds
                .SelectMany(orderId => stopsByOrder[orderId].OrderBy(s => s.Sequence))
                .ToList();
            var executionByStop = executionsByTrip[trip.Id].ToDictionary(e => e.TransportOrderStopId);
            var etaByStop = etasByTrip[trip.Id].ToDictionary(e => e.TransportOrderStopId);

            OperationsStopDto ToDto(StopRow stop)
            {
                var execution = executionByStop.GetValueOrDefault(stop.StopId);
                var eta = etaByStop.GetValueOrDefault(stop.StopId);
                return new OperationsStopDto(
                    stop.StopId, stop.City, stop.LocationName,
                    execution?.Status ?? StopExecutionStatus.Planned,
                    stop.PlannedFrom, stop.PlannedTo,
                    eta?.CurrentEta, eta?.Source, eta?.Status);
            }

            bool IsTerminal(Guid stopId) =>
                executionByStop.TryGetValue(stopId, out var e) && StopStatusMachine.IsTerminal(e.Status);
            bool HasActivity(Guid stopId) =>
                executionByStop.TryGetValue(stopId, out var e) && e.Status != StopExecutionStatus.Planned;

            var completed = tripStops.Count(s => IsTerminal(s.StopId));
            // Current = first stop with activity that is not finished; next = first untouched.
            var currentStop = tripStops.FirstOrDefault(s => HasActivity(s.StopId) && !IsTerminal(s.StopId));
            var nextStop = tripStops.FirstOrDefault(s => !HasActivity(s.StopId) && !IsTerminal(s.StopId)
                                                         && s.StopId != currentStop?.StopId);

            // Worst pending ETA + delay against the planned window.
            var pendingEtas = tripStops
                .Where(s => !IsTerminal(s.StopId))
                .Select(s => new { Stop = s, Eta = etaByStop.GetValueOrDefault(s.StopId) })
                .Where(x => x.Eta is not null)
                .ToList();
            var worst = pendingEtas
                .OrderByDescending(x => x.Eta!.Status)
                .ThenBy(x => x.Eta!.CurrentEta)
                .FirstOrDefault();
            int? delayMinutes = null;
            if (worst?.Eta is { } worstEta && worst.Stop.PlannedTo is { } plannedTo && worstEta.CurrentEta > plannedTo)
            {
                delayMinutes = (int)Math.Round((worstEta.CurrentEta - plannedTo).TotalMinutes);
            }

            var lastScan = lastScanByTrip.GetValueOrDefault(trip.Id);
            var missingPods = tripStops.Count(s =>
                s.StopType == StopType.Unloading && IsTerminal(s.StopId)
                && executionByStop.GetValueOrDefault(s.StopId)?.Status
                    is StopExecutionStatus.Completed or StopExecutionStatus.PartiallyCompleted
                && !podStopsByTrip[trip.Id].Contains(s.StopId));

            items.Add(new OperationsTripDto(
                trip.Id, trip.TripNumber, trip.TripDate, trip.Status,
                trip.DriverId is { } d ? driverNames.GetValueOrDefault(d) : null,
                trip.VehicleId is { } v ? vehicleNumbers.GetValueOrDefault(v) : null,
                trip.TrailerId is { } tr ? trailerNumbers.GetValueOrDefault(tr) : null,
                tripStops.Count, completed,
                currentStop is null ? null : ToDto(currentStop),
                nextStop is null ? null : ToDto(nextStop),
                worst?.Eta?.Status, worst?.Eta?.Source, delayMinutes,
                ResolvePosition(trip, tripStops,
                    stopId => executionByStop.TryGetValue(stopId, out var e) ? e.Status : null,
                    gpsByTrip, nextStop),
                lastScan?.OccurredAt, lastScan?.Result.ToString(),
                openExceptions.GetValueOrDefault(trip.Id),
                missingPods));
        }

        var openCriticalIncidents = await _dbContext.Incidents.AsNoTracking()
            .CountAsync(i => i.TenantId == tenantId && i.Severity == IncidentSeverity.Critical
                             && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress),
                cancellationToken);
        var totalOpenExceptions = await _dbContext.ExecutionExceptions.AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId
                             && (e.Status == ExecutionExceptionStatus.Open
                                 || e.Status == ExecutionExceptionStatus.Investigating
                                 || e.Status == ExecutionExceptionStatus.CustomerActionRequired),
                cancellationToken);
        var activeAlerts = await _dbContext.OperationalAlerts.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.Status != AlertStatus.Resolved, cancellationToken);
        var criticalAlerts = await _dbContext.OperationalAlerts.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.Status != AlertStatus.Resolved
                             && a.Severity == AlertSeverity.Critical, cancellationToken);

        var counters = new OperationsCountersDto(
            items.Count(t => t.Status == TripStatus.InProgress),
            items.Count(t => t.EtaStatus == EtaStatus.Late || t.DelayMinutes > 0),
            totalOpenExceptions,
            openCriticalIncidents,
            items.Sum(t => t.MissingPodCount),
            activeAlerts,
            criticalAlerts);

        return new OperationsOverviewDto(now, counters, items);
    }

    private sealed record StopRow(
        Guid StopId, Guid TransportOrderId, int Sequence, StopType StopType,
        string? City, string? LocationName, DateTime? PlannedFrom, DateTime? PlannedTo,
        decimal? Latitude, decimal? Longitude);

    private sealed record ExecRow(Guid TripId, Guid TransportOrderStopId, StopExecutionStatus Status);

    /// <summary>Honest position ladder: scan GPS → last completed stop → next planned stop → Unavailable.</summary>
    private static TripPositionDto ResolvePosition(
        Trip trip,
        IReadOnlyList<StopRow> tripStops,
        Func<Guid, StopExecutionStatus?> statusOf,
        IReadOnlyDictionary<Guid, Packages.Entities.PackageEvent> gpsByTrip,
        StopRow? nextStop)
    {
        if (gpsByTrip.TryGetValue(trip.Id, out var gps))
        {
            return new TripPositionDto(LocationSource.ScanLocation, gps.Latitude, gps.Longitude,
                gps.OccurredAt, "Laatste scanlocatie");
        }

        // Last completed stop with known master-location coordinates.
        var lastCompleted = tripStops
            .Where(s => statusOf(s.StopId) is { } status
                        && StopStatusMachine.IsTerminal(status)
                        && s.Latitude is not null && s.Longitude is not null)
            .LastOrDefault();
        if (lastCompleted is not null)
        {
            return new TripPositionDto(LocationSource.StopLocation, lastCompleted.Latitude, lastCompleted.Longitude,
                null, $"Laatst afgewerkte stop: {lastCompleted.City ?? lastCompleted.LocationName}");
        }

        if (nextStop is not null && nextStop.Latitude is not null && nextStop.Longitude is not null)
        {
            return new TripPositionDto(LocationSource.PlannedLocation, nextStop.Latitude, nextStop.Longitude,
                null, $"Geplande volgende stop: {nextStop.City ?? nextStop.LocationName}");
        }

        return new TripPositionDto(LocationSource.Unavailable, null, null, null, "Geen locatiegegevens");
    }

    private async Task<Dictionary<Guid, string>> ResolveDriverNamesAsync(
        IReadOnlyCollection<Trip> trips, CancellationToken cancellationToken)
    {
        var driverIds = trips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct().ToList();
        return driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> ResolveAssetNumbersAsync(
        IEnumerable<Guid> ids, bool isVehicle, CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        return isVehicle
            ? await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == _tenantContext.TenantId && idList.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.InternalNumber, cancellationToken)
            : await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == _tenantContext.TenantId && idList.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.InternalNumber, cancellationToken);
    }
}
