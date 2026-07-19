using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Modules.TripCosting.Services;

namespace TransportationService.Api.Modules.Planning.Services;

public class TripService : ITripService
{
    private const string EntityType = "Trip";

    private static readonly IReadOnlyDictionary<TripStatus, TripStatus[]> Transitions =
        new Dictionary<TripStatus, TripStatus[]>
        {
            [TripStatus.Draft] = [TripStatus.Planned, TripStatus.Cancelled],
            [TripStatus.Planned] = [TripStatus.Draft, TripStatus.InProgress, TripStatus.Cancelled],
            [TripStatus.InProgress] = [TripStatus.Completed, TripStatus.Cancelled],
            [TripStatus.Completed] = [],
            [TripStatus.Cancelled] = [],
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IPlanningConflictService _conflictService;
    private readonly INotificationService _notificationService;
    private readonly ITripPlanningSyncService _planningSyncService;
    private readonly ITripCostingService _costingService;

    public TripService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IPlanningConflictService conflictService,
        INotificationService notificationService,
        ITripPlanningSyncService planningSyncService,
        ITripCostingService costingService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _conflictService = conflictService;
        _notificationService = notificationService;
        _planningSyncService = planningSyncService;
        _costingService = costingService;
    }

    /// <summary>
    /// PostgreSQL timestamptz only accepts UTC kinds. API clients may send naive ISO times
    /// (no offset); treat those as UTC instead of failing the save with a 500.
    /// </summary>
    private static DateTime? AsUtc(DateTime? value) => value is { } v
        ? v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime()
        : null;

    private static string? ValidateDistances(decimal? total, decimal? empty)
    {
        if (total is < 0 || empty is < 0)
        {
            return "Afstanden kunnen niet negatief zijn.";
        }

        if (total is { } t && empty is { } e && e > t)
        {
            return "Lege kilometers kunnen niet groter zijn dan de totale afstand.";
        }

        return null;
    }

    private IQueryable<Trip> TenantScoped() =>
        _dbContext.Trips.Where(t => t.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<TripListItemDto>> ListAsync(
        DateOnly? from, DateOnly? to, TripStatus? status, Guid? driverId, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking().Include(t => t.Orders).AsQueryable();

        if (from is { } f) query = query.Where(t => t.TripDate >= f);
        if (to is { } u) query = query.Where(t => t.TripDate <= u);
        if (status is { } s) query = query.Where(t => t.Status == s);
        if (driverId is { } d) query = query.Where(t => t.DriverId == d);

        var trips = await query
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .Take(500)
            .ToListAsync(cancellationToken);

        var names = await ResolveResourceNamesAsync(trips, cancellationToken);

        var items = new List<TripListItemDto>(trips.Count);
        foreach (var trip in trips)
        {
            // Conflicts are recomputed live so the board always reflects the current data.
            var conflicts = await _conflictService.EvaluateAsync(trip, cancellationToken);
            items.Add(new TripListItemDto(
                trip.Id, trip.TripNumber, trip.TripDate, trip.Status,
                trip.DriverId, trip.DriverId is { } dr ? names.Drivers.GetValueOrDefault(dr) : null,
                trip.VehicleId, trip.VehicleId is { } v ? names.Vehicles.GetValueOrDefault(v).Number : null,
                trip.VehicleId is { } v2 ? names.Vehicles.GetValueOrDefault(v2).Plate : null,
                trip.TrailerId, trip.TrailerId is { } tr ? names.Trailers.GetValueOrDefault(tr) : null,
                trip.Orders.Count(o => !o.IsDeleted),
                conflicts.Count(c => c.Blocking)));
        }

        return items;
    }

    public async Task<TripDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await TenantScoped().AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return trip is null ? null : await MapDetailAsync(trip, cancellationToken);
    }

    public async Task<TripOperationResult> CreateAsync(CreateTripRequest request, CancellationToken cancellationToken)
    {
        var referenceError = await ValidateReferencesAsync(
            request.DriverId, request.VehicleId, request.TrailerId, cancellationToken);
        if (referenceError is not null)
        {
            return TripOperationResult.InvalidReference(referenceError);
        }

        var orderError = await ValidateOrdersAsync(request.OrderIds, excludeTripId: null, cancellationToken);
        if (orderError is not null)
        {
            return TripOperationResult.Invalid(orderError);
        }

        if (ValidateDistances(request.PlannedDistanceKm, request.PlannedEmptyKm) is { } distanceError)
        {
            return TripOperationResult.Invalid(distanceError);
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripDate = request.TripDate,
            DriverId = request.DriverId,
            VehicleId = request.VehicleId,
            TrailerId = request.TrailerId,
            PlannedStart = AsUtc(request.PlannedStart),
            PlannedEnd = AsUtc(request.PlannedEnd),
            PlannedDistanceKm = request.PlannedDistanceKm,
            PlannedEmptyKm = request.PlannedEmptyKm,
            Notes = Trim(request.Notes),
            Orders = BuildTripOrders(request.OrderIds),
        };

        _dbContext.Add(trip);
        // Stage the personnel-planning projection so trip + entry land in ONE save.
        var sync = await _planningSyncService.ApplyAsync(trip, cancellationToken);
        // First cost estimate rides in the same save (skips silently without a rate card).
        await _costingService.StageRecalculationAsync(trip, TripCostPhase.Estimated, cancellationToken);
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () =>
            {
                trip.TripNumber = GenerateTripNumber(settings);
                if (sync.Entry is { } entry)
                {
                    entry.TripNumber = trip.TripNumber;
                }
            },
            cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "Created", null,
            new { trip.TripNumber, trip.TripDate, trip.DriverId, trip.VehicleId }, cancellationToken);
        await AuditPlanningSyncAsync(sync, cancellationToken);

        return TripOperationResult.Success(await MapDetailAsync(trip, cancellationToken));
    }

    public async Task<TripOperationResult> UpdateAsync(Guid id, UpdateTripRequest request, CancellationToken cancellationToken)
    {
        var trip = await TenantScoped().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return TripOperationResult.NotFound;
        }

        if (trip.Status != TripStatus.Draft)
        {
            return TripOperationResult.InvalidState(
                "Alleen concept-ritten kunnen worden bewerkt. Zet een geplande rit eerst terug naar concept.");
        }

        var referenceError = await ValidateReferencesAsync(
            request.DriverId, request.VehicleId, request.TrailerId, cancellationToken);
        if (referenceError is not null)
        {
            return TripOperationResult.InvalidReference(referenceError);
        }

        var orderError = await ValidateOrdersAsync(request.OrderIds, excludeTripId: id, cancellationToken);
        if (orderError is not null)
        {
            return TripOperationResult.Invalid(orderError);
        }

        if (ValidateDistances(request.PlannedDistanceKm, request.PlannedEmptyKm) is { } distanceError)
        {
            return TripOperationResult.Invalid(distanceError);
        }

        var before = new { trip.TripDate, trip.DriverId, trip.VehicleId, trip.TrailerId, OrderCount = trip.Orders.Count };

        trip.TripDate = request.TripDate;
        trip.DriverId = request.DriverId;
        trip.VehicleId = request.VehicleId;
        trip.TrailerId = request.TrailerId;
        trip.PlannedStart = AsUtc(request.PlannedStart);
        trip.PlannedEnd = AsUtc(request.PlannedEnd);
        trip.PlannedDistanceKm = request.PlannedDistanceKm;
        trip.PlannedEmptyKm = request.PlannedEmptyKm;
        trip.Notes = Trim(request.Notes);

        _dbContext.RemoveRange(trip.Orders);
        trip.Orders = BuildTripOrders(request.OrderIds);
        foreach (var tripOrder in trip.Orders)
        {
            tripOrder.TripId = trip.Id;
        }

        // New links carry client-generated ids; navigation discovery would attach them as Modified.
        _dbContext.AddRange(trip.Orders);

        // Same save as the trip edit: a driver change MOVES the linked planning entry atomically.
        var sync = await _planningSyncService.ApplyAsync(trip, cancellationToken);
        await _costingService.StageRecalculationAsync(trip, TripCostPhase.Estimated, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "Updated", before,
            new { trip.TripDate, trip.DriverId, trip.VehicleId, trip.TrailerId, OrderCount = trip.Orders.Count }, cancellationToken);
        await AuditPlanningSyncAsync(sync, cancellationToken);

        return TripOperationResult.Success(await MapDetailAsync(trip, cancellationToken));
    }

    public async Task<TripOperationResult> ValidateAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await TenantScoped().AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return trip is null ? TripOperationResult.NotFound : TripOperationResult.Success(await MapDetailAsync(trip, cancellationToken));
    }

    public async Task<TripOperationResult> ChangeStatusAsync(
        Guid id, TripStatus target, bool allowOverride, CancellationToken cancellationToken)
    {
        var trip = await TenantScoped().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return TripOperationResult.NotFound;
        }

        if (!Transitions[trip.Status].Contains(target))
        {
            return TripOperationResult.InvalidState($"Een rit met status '{trip.Status}' kan niet naar '{target}'.");
        }

        if (target == TripStatus.Planned)
        {
            var conflicts = await _conflictService.EvaluateAsync(trip, cancellationToken);
            var blocking = conflicts.Where(c => c.Blocking).ToList();
            if (blocking.Count > 0 && !allowOverride)
            {
                return TripOperationResult.Blocked(blocking);
            }
        }

        var before = new { trip.Status };
        trip.Status = target;

        await PropagateOrderStatusAsync(trip, target, cancellationToken);
        var sync = await _planningSyncService.ApplyAsync(trip, cancellationToken);
        if (target == TripStatus.Completed)
        {
            await _planningSyncService.ApplyActualsAsync(trip.Id, cancellationToken);
            // Execution is done: compute the actual cost pass in the same save.
            await _costingService.StageRecalculationAsync(trip, TripCostPhase.Actual, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "StatusChanged", before,
            new { trip.Status, Overridden = allowOverride }, cancellationToken);
        await AuditPlanningSyncAsync(sync, cancellationToken);

        // Planning a trip tells the assigned driver's user account; a cancellation does too.
        if (target is TripStatus.Planned or TripStatus.Cancelled && trip.DriverId is { } assignedDriver)
        {
            var recipient = await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.Id == assignedDriver && d.TenantId == _tenantContext.TenantId)
                .Join(_dbContext.Users.AsNoTracking().Where(u => u.TenantId == _tenantContext.TenantId),
                    d => d.EmployeeId, u => u.EmployeeId, (d, u) => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (target == TripStatus.Planned)
            {
                await _notificationService.NotifyAsync(recipient, "trip_assigned",
                    "Rit toegewezen",
                    $"Rit {trip.TripNumber} op {trip.TripDate:dd-MM-yyyy} is aan jou toegewezen.",
                    $"/my-trips/{trip.Id}", cancellationToken);
            }
            else
            {
                await _notificationService.NotifyAsync(recipient, "trip_changed",
                    "Rit geannuleerd",
                    $"Rit {trip.TripNumber} op {trip.TripDate:dd-MM-yyyy} is geannuleerd.",
                    "/my-trips", cancellationToken);
            }
        }

        return TripOperationResult.Success(await MapDetailAsync(trip, cancellationToken));
    }

    public async Task<TripOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await TenantScoped().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return TripOperationResult.NotFound;
        }

        if (trip.Status is not (TripStatus.Draft or TripStatus.Cancelled))
        {
            return TripOperationResult.InvalidState("Alleen concept- of geannuleerde ritten kunnen worden verwijderd.");
        }

        _dbContext.RemoveRange(trip.Orders);
        _dbContext.Remove(trip); // soft delete via interceptor
        var sync = await _planningSyncService.ApplyRemovalAsync(trip.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "Deleted",
            new { trip.TripNumber, trip.Status }, null, cancellationToken);
        await AuditPlanningSyncAsync(sync, cancellationToken);

        return TripOperationResult.Success(await MapDetailAsync(trip, cancellationToken));
    }

    /// <summary>Order lifecycle follows the trip: planned/in-progress/completed, released on cancel/revert.</summary>
    private async Task PropagateOrderStatusAsync(Trip trip, TripStatus target, CancellationToken cancellationToken)
    {
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        if (orderIds.Count == 0)
        {
            return;
        }

        var orders = await _dbContext.TransportOrders
            .Where(o => o.TenantId == _tenantContext.TenantId && orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            order.Status = target switch
            {
                TripStatus.Planned when order.Status == TransportOrderStatus.Confirmed => TransportOrderStatus.Planned,
                TripStatus.Draft when order.Status == TransportOrderStatus.Planned => TransportOrderStatus.Confirmed,
                TripStatus.InProgress when order.Status is TransportOrderStatus.Planned or TransportOrderStatus.Confirmed =>
                    TransportOrderStatus.InProgress,
                TripStatus.Completed when order.Status == TransportOrderStatus.InProgress => TransportOrderStatus.Completed,
                TripStatus.Cancelled when order.Status is TransportOrderStatus.Planned or TransportOrderStatus.InProgress =>
                    TransportOrderStatus.Confirmed,
                _ => order.Status,
            };
        }
    }

    private async Task<string?> ValidateReferencesAsync(
        Guid? driverId, Guid? vehicleId, Guid? trailerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (driverId is { } d && !await _dbContext.Drivers.AnyAsync(
                x => x.Id == d && x.TenantId == tenantId, cancellationToken))
        {
            return "De gekoppelde chauffeur bestaat niet.";
        }

        if (vehicleId is { } v && !await _dbContext.Vehicles.AnyAsync(
                x => x.Id == v && x.TenantId == tenantId, cancellationToken))
        {
            return "Het gekoppelde voertuig bestaat niet.";
        }

        if (trailerId is { } t && !await _dbContext.Trailers.AnyAsync(
                x => x.Id == t && x.TenantId == tenantId, cancellationToken))
        {
            return "De gekoppelde oplegger bestaat niet.";
        }

        return null;
    }

    /// <summary>Orders must exist in-tenant, be Confirmed, and not sit on another active trip.</summary>
    private async Task<string?> ValidateOrdersAsync(
        IReadOnlyList<Guid> orderIds, Guid? excludeTripId, CancellationToken cancellationToken)
    {
        if (orderIds.Count != orderIds.Distinct().Count())
        {
            return "Een opdracht kan maar één keer op een rit staan.";
        }

        if (orderIds.Count == 0)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;
        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber, o.Status })
            .ToListAsync(cancellationToken);

        if (orders.Count != orderIds.Count)
        {
            return "Een gekoppelde opdracht bestaat niet.";
        }

        var notConfirmed = orders.Where(o => o.Status != TransportOrderStatus.Confirmed).ToList();
        if (notConfirmed.Count > 0)
        {
            return $"Alleen bevestigde opdrachten kunnen worden ingepland ({string.Join(", ", notConfirmed.Select(o => o.OrderNumber))}).";
        }

        var claimed = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && orderIds.Contains(to.TransportOrderId)
                         && (excludeTripId == null || to.TripId != excludeTripId))
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.Status != TripStatus.Cancelled),
                to => to.TripId, t => t.Id,
                (to, t) => new { to.TransportOrderId, t.TripNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (claimed is not null)
        {
            var number = orders.First(o => o.Id == claimed.TransportOrderId).OrderNumber;
            return $"Opdracht {number} staat al op rit {claimed.TripNumber}.";
        }

        return null;
    }

    private List<TripOrder> BuildTripOrders(IReadOnlyList<Guid> orderIds) =>
        orderIds.Select((orderId, index) => new TripOrder
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TransportOrderId = orderId,
            Sequence = index + 1,
        }).ToList();

    private sealed record ResourceNames(
        Dictionary<Guid, string> Drivers,
        Dictionary<Guid, (string Number, string Plate)> Vehicles,
        Dictionary<Guid, string> Trailers);

    private async Task<ResourceNames> ResolveResourceNamesAsync(
        IReadOnlyCollection<Trip> trips, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var driverIds = trips.Where(t => t.DriverId is not null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var vehicleIds = trips.Where(t => t.VehicleId is not null).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var trailerIds = trips.Where(t => t.TrailerId is not null).Select(t => t.TrailerId!.Value).Distinct().ToList();

        var drivers = driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var vehicles = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == tenantId && vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => (v.InternalNumber, v.LicensePlate), cancellationToken);

        var trailers = trailerIds.Count == 0
            ? []
            : await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && trailerIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.InternalNumber, cancellationToken);

        return new ResourceNames(drivers, vehicles, trailers);
    }

    private async Task<TripDetailDto> MapDetailAsync(Trip trip, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var names = await ResolveResourceNamesAsync([trip], cancellationToken);

        var liveTripOrders = trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence).ToList();
        var orderIds = liveTripOrders.Select(o => o.TransportOrderId).ToList();

        var orderRows = orderIds.Count == 0
            ? []
            : await (from o in _dbContext.TransportOrders.AsNoTracking()
                         .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                     join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                         on o.CustomerId equals c.Id
                     select new { o.Id, o.OrderNumber, CustomerName = c.Name, o.Status, o.GoodsDescription, o.AdrRequired, o.CraneRequired })
                .ToListAsync(cancellationToken);
        var orderById = orderRows.ToDictionary(o => o.Id);

        var stops = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
                .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == tenantId),
                    s => s.LocationId, l => l.Id,
                    (s, locations) => new { Stop = s, Locations = locations })
                .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new
                {
                    x.Stop.TransportOrderId, x.Stop.Sequence, x.Stop.StopType,
                    City = x.Stop.City ?? (l != null ? l.City : null),
                })
                .ToListAsync(cancellationToken);
        var stopsByOrder = stops.ToLookup(s => s.TransportOrderId);

        var orders = liveTripOrders
            .Where(to => orderById.ContainsKey(to.TransportOrderId))
            .Select(to =>
            {
                var order = orderById[to.TransportOrderId];
                var orderStops = stopsByOrder[to.TransportOrderId].OrderBy(s => s.Sequence).ToList();
                return new TripOrderSummaryDto(
                    order.Id, to.Sequence, order.OrderNumber, order.CustomerName, order.Status,
                    order.GoodsDescription,
                    orderStops.FirstOrDefault(s => s.StopType == StopType.Loading)?.City,
                    orderStops.LastOrDefault(s => s.StopType == StopType.Unloading)?.City,
                    order.AdrRequired, order.CraneRequired);
            })
            .ToList();

        var conflicts = await _conflictService.EvaluateAsync(trip, cancellationToken);

        return new TripDetailDto(
            trip.Id, trip.TripNumber, trip.TripDate, trip.Status,
            trip.DriverId, trip.DriverId is { } d ? names.Drivers.GetValueOrDefault(d) : null,
            trip.VehicleId, trip.VehicleId is { } v ? names.Vehicles.GetValueOrDefault(v).Number : null,
            trip.VehicleId is { } v2 ? names.Vehicles.GetValueOrDefault(v2).Plate : null,
            trip.TrailerId, trip.TrailerId is { } tr ? names.Trailers.GetValueOrDefault(tr) : null,
            trip.PlannedStart, trip.PlannedEnd,
            trip.PlannedDistanceKm, trip.PlannedEmptyKm, trip.ActualDistanceKm, trip.ActualEmptyKm,
            trip.Notes,
            orders, conflicts, Transitions[trip.Status]);
    }

    private async Task AuditPlanningSyncAsync(TripPlanningSyncResult sync, CancellationToken cancellationToken)
    {
        if (sync.Action == TripPlanningSyncAction.None || sync.EntryId is null)
        {
            return;
        }

        await _auditService.RecordAsync("TripPlanningEntry", sync.EntryId.Value.ToString(), sync.Action.ToString(),
            sync.PreviousEmployeeId is { } previous ? new { EmployeeId = previous } : null,
            sync.Entry is { } entry
                ? new { entry.TripId, entry.TripNumber, entry.EmployeeId, entry.Date, entry.Status }
                : null,
            cancellationToken);
    }

    private static string GenerateTripNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"RIT-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }

        var number = $"{settings.TripNumberPrefix}{settings.TripNumberNextValue:0000}";
        settings.TripNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
