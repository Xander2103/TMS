using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
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
    private readonly TimeProvider _timeProvider;

    public TripExecutionService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        ITripService tripService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _tripService = tripService;
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
                stops.Count(s => s.Status is StopExecutionStatus.Completed or StopExecutionStatus.Skipped)));
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

    public Task<ExecutionResult> ArriveAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        MutateAsync(tripId, stopId, restrictToOwnDriver, (execution, now) =>
        {
            if (execution.Status is StopExecutionStatus.Completed or StopExecutionStatus.Skipped)
            {
                return "Deze stop is al afgehandeld.";
            }

            execution.Status = StopExecutionStatus.Arrived;
            execution.ArrivedAt ??= now;
            return null;
        }, "Arrived", cancellationToken);

    public Task<ExecutionResult> CompleteAsync(
        Guid tripId, Guid stopId, CompleteStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        MutateAsync(tripId, stopId, restrictToOwnDriver, (execution, now) =>
        {
            if (execution.Status == StopExecutionStatus.Skipped)
            {
                return "Een overgeslagen stop kan niet meer worden afgerond.";
            }

            execution.Status = StopExecutionStatus.Completed;
            execution.ArrivedAt ??= now;
            execution.CompletedAt = now;
            execution.PodSignedBy = Trim(request.PodSignedBy);
            execution.Remarks = Trim(request.Remarks);
            return null;
        }, "Completed", cancellationToken);

    public Task<ExecutionResult> SkipAsync(
        Guid tripId, Guid stopId, SkipStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Remarks)
            ? Task.FromResult(ExecutionResult.Invalid("Een reden is verplicht bij het overslaan van een stop."))
            : MutateAsync(tripId, stopId, restrictToOwnDriver, (execution, now) =>
            {
                if (execution.Status == StopExecutionStatus.Completed)
                {
                    return "Een afgeronde stop kan niet meer worden overgeslagen.";
                }

                execution.Status = StopExecutionStatus.Skipped;
                execution.CompletedAt = now;
                execution.Remarks = request.Remarks.Trim();
                return null;
            }, "Skipped", cancellationToken);

    private async Task<ExecutionResult> MutateAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver,
        Func<StopExecution, DateTime, string?> apply, string auditAction, CancellationToken cancellationToken)
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

        // The stop must belong to one of the orders on this trip.
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        var stopBelongs = await _dbContext.TransportOrderStops.AsNoTracking()
            .AnyAsync(s => s.Id == stopId && s.TenantId == _tenantContext.TenantId
                           && orderIds.Contains(s.TransportOrderId), cancellationToken);
        if (!stopBelongs)
        {
            return ExecutionResult.NotFound;
        }

        var execution = await _dbContext.StopExecutions
            .FirstOrDefaultAsync(e => e.TripId == tripId && e.TransportOrderStopId == stopId
                                      && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (execution is null)
        {
            execution = new StopExecution
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TripId = tripId,
                TransportOrderStopId = stopId,
            };
            _dbContext.Add(execution);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var error = apply(execution, now);
        if (error is not null)
        {
            return ExecutionResult.InvalidState(error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, execution.Id.ToString(), auditAction, null,
            new { execution.TripId, execution.TransportOrderStopId, execution.Status }, cancellationToken);

        // Completing/skipping the final open stop finishes the whole trip (orders follow).
        var stops = await LoadExecutionStopsAsync(trip, cancellationToken);
        if (stops.Count > 0 && stops.All(s => s.Status is StopExecutionStatus.Completed or StopExecutionStatus.Skipped))
        {
            await _tripService.ChangeStatusAsync(trip.Id, TripStatus.Completed, allowOverride: false, cancellationToken);
        }

        return await GetExecutionAsync(tripId, restrictToOwnDriver: false, cancellationToken);
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
        DateTime? PlannedFrom, DateTime? PlannedTo, string? Instructions,
        StopExecutionStatus Status, DateTime? ArrivedAt, DateTime? CompletedAt,
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
                return new ExecutionStopRow(
                    x.Stop.Id, x.Stop.TransportOrderId, order.OrderNumber, order.CustomerName,
                    orderSequence[x.Stop.TransportOrderId], x.Stop.Sequence, x.Stop.StopType,
                    x.Location?.Name ?? x.Stop.LocationName ?? x.Stop.City ?? string.Empty,
                    x.Stop.Address ?? (string.IsNullOrWhiteSpace(locationAddress) ? null : locationAddress),
                    x.Stop.PostalCode ?? x.Location?.PostalCode,
                    x.Stop.City ?? x.Location?.City,
                    x.Stop.PlannedFrom, x.Stop.PlannedTo, x.Stop.Instructions,
                    execution?.Status ?? StopExecutionStatus.Pending,
                    execution?.ArrivedAt, execution?.CompletedAt,
                    execution?.PodPath is not null, execution?.PodSignedBy, execution?.Remarks);
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
                r.PlannedFrom, r.PlannedTo, r.Instructions,
                r.Status, r.ArrivedAt, r.CompletedAt, r.HasPod, r.PodSignedBy, r.Remarks)).ToList(),
            stops.Count(s => s.Status is StopExecutionStatus.Completed or StopExecutionStatus.Skipped),
            stops.Count);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
