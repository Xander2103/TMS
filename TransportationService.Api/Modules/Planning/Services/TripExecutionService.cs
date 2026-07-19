using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

public class TripExecutionService : ITripExecutionService
{
    private const string EntityType = "StopExecution";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly ITripService _tripService;
    private readonly ITripPlanningSyncService _planningSyncService;
    private readonly TimeProvider _timeProvider;

    public TripExecutionService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        ITripService tripService,
        ITripPlanningSyncService planningSyncService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _tripService = tripService;
        _planningSyncService = planningSyncService;
        _timeProvider = timeProvider;
    }

    /// <summary>Driver id of the logged-in user, resolved via the user's employee link; null when not a driver.</summary>
    private async Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.EmployeeId != null)
            .Join(_dbContext.Drivers.AsNoTracking().Where(d => d.TenantId == _tenantContext.TenantId),
                u => u.EmployeeId, d => d.EmployeeId,
                (u, d) => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MyTripDto>> ListMyTripsAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return [];
        }

        var rangeFrom = from ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var rangeTo = to ?? rangeFrom.AddDays(7);

        var trips = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .Where(t => t.TenantId == _tenantContext.TenantId && t.DriverId == driverId
                        && t.TripDate >= rangeFrom && t.TripDate <= rangeTo
                        && t.Status != TripStatus.Cancelled && t.Status != TripStatus.Draft)
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .ToListAsync(cancellationToken);

        var result = new List<MyTripDto>(trips.Count);
        foreach (var trip in trips)
        {
            var stops = await LoadExecutionStopsAsync(trip, cancellationToken);
            var vehicle = trip.VehicleId is { } v
                ? await _dbContext.Vehicles.AsNoTracking()
                    .Where(x => x.Id == v && x.TenantId == _tenantContext.TenantId)
                    .Select(x => new { x.InternalNumber, x.LicensePlate })
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            var trailerNumber = trip.TrailerId is { } tr
                ? await _dbContext.Trailers.AsNoTracking()
                    .Where(x => x.Id == tr && x.TenantId == _tenantContext.TenantId)
                    .Select(x => x.InternalNumber)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            result.Add(new MyTripDto(
                trip.Id, trip.TripNumber, trip.TripDate, trip.Status,
                vehicle?.InternalNumber, vehicle?.LicensePlate, trailerNumber,
                trip.Orders.Count(o => !o.IsDeleted),
                stops.Count,
                stops.Count(s => StopStatusMachine.IsTerminal(s.Status))));
        }

        return result;
    }

    public async Task<ExecutionResult> GetExecutionAsync(
        Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var (trip, guard) = await LoadGuardedAsync(tripId, restrictToOwnDriver, cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        return ExecutionResult.Success(await MapExecutionAsync(trip!, cancellationToken));
    }

    public Task<ExecutionResult> TransitionAsync(
        Guid tripId, Guid stopId, TransitionStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        TransitionCoreAsync(tripId, stopId, request, restrictToOwnDriver, beforeSave: null, cancellationToken);

    public Task<ExecutionResult> ArriveAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        TransitionCoreAsync(tripId, stopId, new TransitionStopRequest(StopExecutionStatus.Arrived),
            restrictToOwnDriver, beforeSave: null, cancellationToken);

    public Task<ExecutionResult> CompleteAsync(
        Guid tripId, Guid stopId, CompleteStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        TransitionCoreAsync(tripId, stopId,
            new TransitionStopRequest(StopExecutionStatus.Completed, Reason: request.Reason, Notes: request.Remarks),
            restrictToOwnDriver,
            beforeSave: execution => execution.PodSignedBy = Trim(request.PodSignedBy),
            cancellationToken);

    public Task<ExecutionResult> SkipAsync(
        Guid tripId, Guid stopId, SkipStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        TransitionCoreAsync(tripId, stopId,
            new TransitionStopRequest(StopExecutionStatus.Skipped, Reason: request.Remarks),
            restrictToOwnDriver, beforeSave: null, cancellationToken);

    public async Task<StopHistoryResult> GetStopHistoryAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var (trip, guard) = await LoadGuardedAsync(tripId, restrictToOwnDriver, cancellationToken);
        if (guard is not null)
        {
            return guard.Outcome == ExecutionOutcome.NotYourTrip ? StopHistoryResult.NotYourTrip : StopHistoryResult.NotFound;
        }

        if (!await StopBelongsToTripAsync(trip!, stopId, cancellationToken))
        {
            return StopHistoryResult.NotFound;
        }

        var executionId = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TripId == tripId && e.TransportOrderStopId == stopId && e.TenantId == _tenantContext.TenantId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (executionId is null)
        {
            return StopHistoryResult.Success([]);
        }

        // ToStatus only ever moves forward along the machine, so it is a stable tiebreaker for
        // the same-timestamp rows a bridged transition writes.
        var rows = await _dbContext.StopStatusHistories.AsNoTracking()
            .Where(x => x.StopExecutionId == executionId && x.TenantId == _tenantContext.TenantId)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.ToStatus)
            .GroupJoin(_dbContext.Users.AsNoTracking().Where(u => u.TenantId == _tenantContext.TenantId),
                x => x.UserId, u => u.Id,
                (x, users) => new { Row = x, Users = users })
            .SelectMany(x => x.Users.DefaultIfEmpty(), (x, u) => new
            {
                x.Row.FromStatus, x.Row.ToStatus, x.Row.OccurredAt, x.Row.Reason,
                UserName = u == null ? null : u.FirstName + " " + u.LastName,
            })
            .ToListAsync(cancellationToken);

        return StopHistoryResult.Success(rows
            .Select(x => new StopStatusHistoryDto(x.FromStatus, x.ToStatus, x.OccurredAt, x.UserName, x.Reason))
            .ToList());
    }

    private async Task<ExecutionResult> TransitionCoreAsync(
        Guid tripId, Guid stopId, TransitionStopRequest request, bool restrictToOwnDriver,
        Action<StopExecution>? beforeSave, CancellationToken cancellationToken)
    {
        var (trip, guard) = await LoadGuardedAsync(tripId, restrictToOwnDriver, cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (trip!.Status != TripStatus.InProgress)
        {
            return ExecutionResult.InvalidState("Stops kunnen alleen worden geregistreerd terwijl de rit onderweg is.");
        }

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        var stop = await _dbContext.TransportOrderStops.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stopId && s.TenantId == _tenantContext.TenantId
                                      && orderIds.Contains(s.TransportOrderId), cancellationToken);
        if (stop is null)
        {
            return ExecutionResult.NotFound;
        }

        var execution = await _dbContext.StopExecutions
            .FirstOrDefaultAsync(e => e.TripId == tripId && e.TransportOrderStopId == stopId
                                      && e.TenantId == _tenantContext.TenantId, cancellationToken);
        var isNewExecution = execution is null;
        execution ??= new StopExecution
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = tripId,
            TransportOrderStopId = stopId,
        };

        var from = execution.Status;
        var target = request.ToStatus;

        if (!StopStatusMachine.IsAllowed(from, target, stop.StopType, out var viaBridge))
        {
            var allowed = StopStatusMachine.AllowedTargets(from, stop.StopType);
            var allowedText = allowed.Count == 0 ? "geen (eindstatus)" : string.Join(", ", allowed);
            return ExecutionResult.InvalidState(
                $"Een stop met status '{from}' kan niet naar '{target}'. Toegestaan: {allowedText}.");
        }

        var reason = Trim(request.Reason);

        if (StopStatusMachine.RequiresReason(target) && reason is null)
        {
            var noun = target switch
            {
                StopExecutionStatus.Skipped => "het overslaan van een stop",
                StopExecutionStatus.Failed => "een mislukte stop",
                _ => "een gedeeltelijk afgewerkte stop",
            };
            return ExecutionResult.Invalid($"Een reden is verplicht bij {noun}.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Arrival (explicit or bridged) past the latest bound must be explained.
        if (StopStatusMachine.RecordsArrival(target, viaBridge) && execution.ArrivedAt is null)
        {
            var latestBound = stop.LatestAllowed ?? stop.ConfirmedTo ?? stop.PlannedTo;
            if (latestBound is { } bound && now > bound)
            {
                if (reason is null)
                {
                    return ExecutionResult.Invalid(
                        "Je komt aan na het uiterste tijdstip van deze stop; een reden voor de late aankomst is verplicht.");
                }

                execution.LateArrivalReason = reason;
            }
        }

        // Attach only after every validation gate passed, so a rejected call leaves no
        // phantom Added row behind for the next attempt.
        if (isNewExecution)
        {
            _dbContext.Add(execution);
        }

        var userId = _currentUserContext.CurrentUserId;
        var steps = viaBridge
            ? new[] { (From: from, To: StopExecutionStatus.Arrived), (From: StopExecutionStatus.Arrived, To: target) }
            : [(From: from, To: target)];

        foreach (var step in steps)
        {
            if (step.To == StopExecutionStatus.Arrived)
            {
                execution.ArrivedAt ??= now;
            }

            _dbContext.StopStatusHistories.Add(new StopStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                StopExecutionId = execution.Id,
                FromStatus = step.From,
                ToStatus = step.To,
                OccurredAt = now,
                UserId = userId,
                // The reason belongs to the requested step; the bridged arrival step only
                // carries it when it explains a late arrival.
                Reason = step.To == target ? reason : execution.LateArrivalReason,
            });
        }

        execution.Status = target;

        if (target is StopExecutionStatus.Completed or StopExecutionStatus.PartiallyCompleted or StopExecutionStatus.Failed)
        {
            execution.CompletedAt = now;
            execution.DepartedAt ??= now;
        }

        if (StopStatusMachine.RequiresReason(target))
        {
            execution.StatusReason = reason;
        }

        if (Trim(request.Notes) is { } notes)
        {
            execution.Remarks = notes;
        }

        beforeSave?.Invoke(execution);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, execution.Id.ToString(), target.ToString(),
            new { Status = from },
            new { execution.TripId, execution.TransportOrderStopId, execution.Status, Reason = reason }, cancellationToken);

        // Keep the personnel-planning entry's actual times fresh while the trip runs.
        var actualsSync = await _planningSyncService.ApplyActualsAsync(trip.Id, cancellationToken);
        if (actualsSync.Action != TripPlanningSyncAction.None)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Completing/skipping/failing the final open stop finishes the whole trip (orders follow).
        var stops = await LoadExecutionStopsAsync(trip, cancellationToken);
        if (stops.Count > 0 && stops.All(s => StopStatusMachine.IsTerminal(s.Status)))
        {
            await _tripService.ChangeStatusAsync(trip.Id, TripStatus.Completed, allowOverride: false, cancellationToken);
        }

        return await GetExecutionAsync(tripId, restrictToOwnDriver: false, cancellationToken);
    }

    private async Task<bool> StopBelongsToTripAsync(Trip trip, Guid stopId, CancellationToken cancellationToken)
    {
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        return await _dbContext.TransportOrderStops.AsNoTracking()
            .AnyAsync(s => s.Id == stopId && s.TenantId == _tenantContext.TenantId
                           && orderIds.Contains(s.TransportOrderId), cancellationToken);
    }

    private async Task<(Trip? Trip, ExecutionResult? Guard)> LoadGuardedAsync(
        Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return (null, ExecutionResult.NotFound);
        }

        if (restrictToOwnDriver)
        {
            var driverId = await CurrentDriverIdAsync(cancellationToken);
            if (driverId is null || trip.DriverId != driverId)
            {
                return (null, ExecutionResult.NotYourTrip);
            }
        }

        return (trip, null);
    }

    private sealed record ExecutionStopRow(
        Guid StopId, Guid OrderId, string OrderNumber, string CustomerName, int OrderSequence, int StopSequence,
        StopType StopType, string LocationName, string? Address, string? PostalCode, string? City,
        DateTime? PlannedFrom, DateTime? PlannedTo, DateTime? RequestedFrom, DateTime? RequestedTo,
        DateTime? ConfirmedFrom, DateTime? ConfirmedTo, DateTime? EarliestAllowed, DateTime? LatestAllowed,
        bool AppointmentRequired, string? AppointmentReference,
        string? Instructions, string? AccessInstructions, string? LoadingInstructions, string? UnloadingInstructions,
        StopExecutionStatus Status, DateTime? ArrivedAt, DateTime? DepartedAt, DateTime? CompletedAt,
        int? WaitingMinutes, string? LateArrivalReason, string? StatusReason,
        bool HasPod, string? PodSignedBy, string? Remarks);

    private async Task<List<ExecutionStopRow>> LoadExecutionStopsAsync(Trip trip, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tripOrders = trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence).ToList();
        var orderIds = tripOrders.Select(o => o.TransportOrderId).ToList();
        if (orderIds.Count == 0)
        {
            return [];
        }

        var orders = await (from o in _dbContext.TransportOrders.AsNoTracking()
                                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                            join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                                on o.CustomerId equals c.Id
                            select new { o.Id, o.OrderNumber, CustomerName = c.Name })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var stops = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
            .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == tenantId),
                s => s.LocationId, l => l.Id,
                (s, locations) => new { Stop = s, Locations = locations })
            .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new { x.Stop, Location = l })
            .ToListAsync(cancellationToken);

        var executions = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TripId == trip.Id)
            .ToDictionaryAsync(e => e.TransportOrderStopId, cancellationToken);

        // Stops with a finalised current POD (Wave 4) — the legacy PodPath keeps counting too.
        var podStopIds = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TripId == trip.Id && p.IsCurrent)
            .Select(p => p.TransportOrderStopId)
            .ToListAsync(cancellationToken);

        // First handling-start per execution: turns arrival -> handling into a waiting time.
        var executionIds = executions.Values.Select(e => e.Id).ToList();
        var handlingStarts = executionIds.Count == 0
            ? []
            : await _dbContext.StopStatusHistories.AsNoTracking()
                .Where(h => h.TenantId == tenantId && executionIds.Contains(h.StopExecutionId)
                            && (h.ToStatus == StopExecutionStatus.Loading || h.ToStatus == StopExecutionStatus.Unloading))
                .GroupBy(h => h.StopExecutionId)
                .Select(g => new { StopExecutionId = g.Key, StartedAt = g.Min(h => h.OccurredAt) })
                .ToDictionaryAsync(x => x.StopExecutionId, x => x.StartedAt, cancellationToken);

        var orderSequence = tripOrders
            .Select((o, index) => (o.TransportOrderId, Sequence: o.Sequence, Index: index))
            .ToDictionary(x => x.TransportOrderId, x => x.Sequence);

        return stops
            .Where(x => orders.ContainsKey(x.Stop.TransportOrderId))
            .Select(x =>
            {
                var order = orders[x.Stop.TransportOrderId];
                var execution = executions.GetValueOrDefault(x.Stop.Id);
                var locationAddress = x.Location is null
                    ? null
                    : string.Join(" ", new[] { x.Location.Street, x.Location.HouseNumber }.Where(p => !string.IsNullOrWhiteSpace(p)));

                int? waitingMinutes = null;
                if (execution?.ArrivedAt is { } arrivedAt)
                {
                    var handlingEnd = handlingStarts.TryGetValue(execution.Id, out var started)
                        ? started
                        : execution.DepartedAt;
                    if (handlingEnd is { } end && end >= arrivedAt)
                    {
                        waitingMinutes = (int)Math.Round((end - arrivedAt).TotalMinutes);
                    }
                }

                var status = execution?.Status ?? StopExecutionStatus.Planned;

                return new ExecutionStopRow(
                    x.Stop.Id, x.Stop.TransportOrderId, order.OrderNumber, order.CustomerName,
                    orderSequence[x.Stop.TransportOrderId], x.Stop.Sequence, x.Stop.StopType,
                    x.Location?.Name ?? x.Stop.LocationName ?? x.Stop.City ?? string.Empty,
                    x.Stop.Address ?? (string.IsNullOrWhiteSpace(locationAddress) ? null : locationAddress),
                    x.Stop.PostalCode ?? x.Location?.PostalCode,
                    x.Stop.City ?? x.Location?.City,
                    x.Stop.PlannedFrom, x.Stop.PlannedTo, x.Stop.RequestedFrom, x.Stop.RequestedTo,
                    x.Stop.ConfirmedFrom, x.Stop.ConfirmedTo, x.Stop.EarliestAllowed, x.Stop.LatestAllowed,
                    x.Stop.AppointmentRequired || (x.Location?.AppointmentRequired ?? false),
                    x.Stop.AppointmentReference,
                    x.Stop.Instructions,
                    x.Stop.AccessInstructions ?? x.Location?.AccessInstructions,
                    x.Stop.LoadingInstructions ?? x.Location?.LoadingInstructions,
                    x.Stop.UnloadingInstructions ?? x.Location?.UnloadingInstructions,
                    status,
                    execution?.ArrivedAt, execution?.DepartedAt, execution?.CompletedAt,
                    waitingMinutes, execution?.LateArrivalReason, execution?.StatusReason,
                    execution?.PodPath is not null || podStopIds.Contains(x.Stop.Id),
                    execution?.PodSignedBy, execution?.Remarks);
            })
            .OrderBy(r => r.OrderSequence).ThenBy(r => r.StopSequence)
            .ToList();
    }

    private async Task<TripExecutionDto> MapExecutionAsync(Trip trip, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        // The trip may have transitioned (auto-complete); read the current status.
        var currentStatus = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.Id == trip.Id)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(cancellationToken);

        var driverName = trip.DriverId is { } d
            ? await _dbContext.Drivers.AsNoTracking()
                .Where(x => x.Id == d && x.TenantId == tenantId)
                .Join(_dbContext.Employees.AsNoTracking(), x => x.EmployeeId, e => e.Id,
                    (x, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var vehicle = trip.VehicleId is { } v
            ? await _dbContext.Vehicles.AsNoTracking()
                .Where(x => x.Id == v && x.TenantId == tenantId)
                .Select(x => new { x.InternalNumber, x.LicensePlate })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var stops = await LoadExecutionStopsAsync(trip, cancellationToken);

        return new TripExecutionDto(
            trip.Id, trip.TripNumber, trip.TripDate, currentStatus,
            driverName, vehicle?.InternalNumber, vehicle?.LicensePlate,
            stops.Select(r => new ExecutionStopDto(
                r.StopId, r.OrderId, r.OrderNumber, r.CustomerName, r.OrderSequence, r.StopSequence,
                r.StopType, r.LocationName, r.Address, r.PostalCode, r.City,
                r.PlannedFrom, r.PlannedTo, r.RequestedFrom, r.RequestedTo,
                r.ConfirmedFrom, r.ConfirmedTo, r.EarliestAllowed, r.LatestAllowed,
                r.AppointmentRequired, r.AppointmentReference,
                r.Instructions, r.AccessInstructions, r.LoadingInstructions, r.UnloadingInstructions,
                r.Status, r.ArrivedAt, r.DepartedAt, r.CompletedAt,
                r.WaitingMinutes, r.LateArrivalReason, r.StatusReason,
                StopStatusMachine.AllowedTargets(r.Status, r.StopType),
                r.HasPod, r.PodSignedBy, r.Remarks)).ToList(),
            stops.Count(s => StopStatusMachine.IsTerminal(s.Status)),
            stops.Count);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
