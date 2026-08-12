using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Eta.Services;

public record StopEtaDto(
    Guid TransportOrderStopId,
    string StopLabel,
    StopType StopType,
    DateTime CurrentEta,
    EtaSource Source,
    EtaStatus Status,
    DateTime? LatestBound,
    string? Note);

public record TripEtaDto(
    Guid TripId,
    string TripNumber,
    int ManualDelayMinutes,
    string? DelayReason,
    IReadOnlyList<StopEtaDto> Stops);

public record StopEtaHistoryDto(DateTime Eta, EtaSource Source, EtaStatus Status, DateTime RecordedAt);

public interface IEtaService
{
    /// <summary>Recomputes ETAs for all pending stops; overrides stay; history/notifications only on real change.</summary>
    Task<TripEtaDto?> RecalculateTripAsync(Guid tripId, CancellationToken cancellationToken);

    Task<TripEtaDto?> SetTripDelayAsync(Guid tripId, int minutes, string? reason, CancellationToken cancellationToken);

    Task<TripEtaDto?> OverrideStopEtaAsync(Guid tripId, Guid stopId, DateTime eta, string? note, CancellationToken cancellationToken);

    Task<TripEtaDto?> ClearStopEtaOverrideAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StopEtaHistoryDto>?> GetStopEtaHistoryAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken);

    /// <summary>P8: route start — seeds/refreshes the trip's ETAs and queues one
    /// driver_en_route message per customer order on the trip (idempotent per trip+order).</summary>
    Task NotifyTripStartedAsync(Guid tripId, CancellationToken cancellationToken);
}

public class EtaService : IEtaService
{
    private const string EntityType = "StopEta";

    /// <summary>Sequential heuristic between stops; honest raming, geen routeoptimalisatie.</summary>
    private const int HeuristicTravelMinutes = 30;

    private static readonly TimeSpan AtRiskWindow = TimeSpan.FromMinutes(15);

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IRouteEstimationProvider _routeProvider;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly IMessageOutboxService _messageOutbox;
    private readonly TimeProvider _timeProvider;

    public EtaService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IRouteEstimationProvider routeProvider,
        IAuditService auditService,
        INotificationService notificationService,
        IMessageOutboxService messageOutbox,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _routeProvider = routeProvider;
        _auditService = auditService;
        _notificationService = notificationService;
        _messageOutbox = messageOutbox;
        _timeProvider = timeProvider;
    }

    public async Task<TripEtaDto?> RecalculateTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence)
            .Select(o => o.TransportOrderId).ToList();

        var stops = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
            .ToListAsync(cancellationToken);
        var orderSequence = orderIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);

        var terminalStopIds = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TripId == tripId)
            .ToListAsync(cancellationToken);
        var pending = stops
            .Where(s => !StopStatusMachine.IsTerminal(
                terminalStopIds.FirstOrDefault(e => e.TransportOrderStopId == s.Id)?.Status ?? StopExecutionStatus.Planned))
            .OrderBy(s => orderSequence[s.TransportOrderId]).ThenBy(s => s.Sequence)
            .ToList();

        var settings = await _dbContext.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        var loadingMinutes = settings?.DefaultLoadingMinutes ?? 30;
        var unloadingMinutes = settings?.DefaultUnloadingMinutes ?? 30;
        // Wave 8: historical stop-duration estimates — measured reality per LOCATION beats the
        // tenant default (≥3 samples over the last 90 days, clamped to a sane band).
        var historicalMinutes = await HistoricalHandlingMinutesAsync(pending, cancellationToken);

        var providerMinutes = await _routeProvider.EstimateTravelMinutesAsync(
            new RouteEstimationRequest(pending
                .Select(s => new RouteEstimationStop(s.Id, null, null, s.City))
                .ToList()),
            cancellationToken);
        var source = providerMinutes is not null ? EtaSource.Provider : EtaSource.Heuristic;

        var existing = await _dbContext.StopEtas
            .Where(e => e.TenantId == tenantId && e.TripId == tripId)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cursor = now.AddMinutes(trip.ManualDelayMinutes);

        var results = new List<StopEtaDto>(pending.Count);
        for (var index = 0; index < pending.Count; index += 1)
        {
            var stop = pending[index];
            var eta = existing.FirstOrDefault(e => e.TransportOrderStopId == stop.Id);

            if (eta?.Source == EtaSource.DispatcherOverride)
            {
                // Overrides stay; the cursor continues from the override so later stops follow it.
                cursor = eta.CurrentEta.AddMinutes(EffectiveHandlingMinutes(stop, historicalMinutes, loadingMinutes, unloadingMinutes));
                results.Add(Map(stop, eta));
                continue;
            }

            var travel = providerMinutes is not null && index < providerMinutes.Count
                ? providerMinutes[index]
                : HeuristicTravelMinutes;
            var newEta = cursor.AddMinutes(travel);
            var status = ResolveStatus(newEta, LatestBound(stop));

            if (eta is null)
            {
                eta = new StopEta
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    TripId = tripId,
                    TransportOrderStopId = stop.Id,
                    CurrentEta = newEta,
                    Source = source,
                    Status = status,
                };
                _dbContext.Add(eta);
                await RecordChangeAsync(eta, stop, previousStatus: null, cancellationToken);
            }
            else if (Math.Abs((eta.CurrentEta - newEta).TotalMinutes) >= 1 || eta.Status != status)
            {
                var previousStatus = eta.Status;
                eta.CurrentEta = newEta;
                eta.Source = source;
                eta.Status = status;
                await RecordChangeAsync(eta, stop, previousStatus, cancellationToken);
            }

            cursor = eta.CurrentEta.AddMinutes(EffectiveHandlingMinutes(stop, historicalMinutes, loadingMinutes, unloadingMinutes));
            results.Add(Map(stop, eta));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new TripEtaDto(trip.Id, trip.TripNumber, trip.ManualDelayMinutes, trip.DelayReason, results);
    }

    public async Task<TripEtaDto?> SetTripDelayAsync(
        Guid tripId, int minutes, string? reason, CancellationToken cancellationToken)
    {
        var trip = await _dbContext.Trips
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var before = new { trip.ManualDelayMinutes, trip.DelayReason };
        trip.ManualDelayMinutes = Math.Max(0, minutes);
        trip.DelayReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "DelaySet", before,
            new { trip.ManualDelayMinutes, trip.DelayReason }, cancellationToken);

        return await RecalculateTripAsync(tripId, cancellationToken);
    }

    public async Task<TripEtaDto?> OverrideStopEtaAsync(
        Guid tripId, Guid stopId, DateTime eta, string? note, CancellationToken cancellationToken)
    {
        var stopEta = await _dbContext.StopEtas
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId
                                      && e.TransportOrderStopId == stopId, cancellationToken);
        var stop = await _dbContext.TransportOrderStops.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stopId && s.TenantId == _tenantContext.TenantId, cancellationToken);
        if (stop is null)
        {
            return null;
        }

        if (stopEta is null)
        {
            stopEta = new StopEta
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TripId = tripId,
                TransportOrderStopId = stopId,
            };
            _dbContext.Add(stopEta);
        }

        var previousStatus = stopEta.Status;
        stopEta.CurrentEta = eta;
        stopEta.Source = EtaSource.DispatcherOverride;
        stopEta.Status = ResolveStatus(eta, LatestBound(stop));
        stopEta.OverriddenByUserId = _currentUserContext.CurrentUserId;
        stopEta.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await RecordChangeAsync(stopEta, stop, previousStatus, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, stopEta.Id.ToString(), "Overridden", null,
            new { stopEta.TripId, stopEta.TransportOrderStopId, stopEta.CurrentEta, stopEta.Note }, cancellationToken);

        return await RecalculateTripAsync(tripId, cancellationToken);
    }

    public async Task<TripEtaDto?> ClearStopEtaOverrideAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var stopEta = await _dbContext.StopEtas
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId
                                      && e.TransportOrderStopId == stopId, cancellationToken);
        if (stopEta is null)
        {
            return await RecalculateTripAsync(tripId, cancellationToken);
        }

        stopEta.Source = EtaSource.Heuristic;
        stopEta.OverriddenByUserId = null;
        stopEta.Note = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, stopEta.Id.ToString(), "OverrideCleared", null, null, cancellationToken);

        return await RecalculateTripAsync(tripId, cancellationToken);
    }

    public async Task<IReadOnlyList<StopEtaHistoryDto>?> GetStopEtaHistoryAsync(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var etaId = await _dbContext.StopEtas.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId
                        && e.TransportOrderStopId == stopId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (etaId is null)
        {
            return [];
        }

        return await _dbContext.StopEtaHistories.AsNoTracking()
            .Where(h => h.StopEtaId == etaId && h.TenantId == _tenantContext.TenantId)
            .OrderBy(h => h.RecordedAt).ThenBy(h => h.Eta)
            .Select(h => new StopEtaHistoryDto(h.Eta, h.Source, h.Status, h.RecordedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Wave 8: measured average handling minutes per LOCATION (DepartedAt − ArrivedAt,
    /// last 90 days, ≥3 samples, clamped 5..240) — reality beats the tenant default.</summary>
    private async Task<IReadOnlyDictionary<Guid, int>> HistoricalHandlingMinutesAsync(
        IReadOnlyList<TransportOrderStop> pending, CancellationToken cancellationToken)
    {
        var locationIds = pending.Where(s => s.LocationId != null).Select(s => s.LocationId!.Value).Distinct().ToList();
        if (locationIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var tenantId = _tenantContext.TenantId;
        var since = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-90);
        var samples = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ArrivedAt != null && e.DepartedAt != null && e.ArrivedAt >= since)
            .Join(_dbContext.TransportOrderStops.AsNoTracking()
                    .Where(s => s.LocationId != null && locationIds.Contains(s.LocationId.Value)),
                e => e.TransportOrderStopId, s => s.Id,
                (e, s) => new { LocationId = s.LocationId!.Value, e.ArrivedAt, e.DepartedAt })
            .ToListAsync(cancellationToken);

        return samples
            .Select(s => new { s.LocationId, Minutes = (s.DepartedAt!.Value - s.ArrivedAt!.Value).TotalMinutes })
            .Where(s => s.Minutes is > 0 and < 600)
            .GroupBy(s => s.LocationId)
            .Where(g => g.Count() >= 3)
            .ToDictionary(g => g.Key, g => Math.Clamp((int)Math.Round(g.Average(s => s.Minutes)), 5, 240));
    }

    private static int EffectiveHandlingMinutes(
        TransportOrderStop stop, IReadOnlyDictionary<Guid, int> historical, int loading, int unloading) =>
        stop.LocationId is { } locationId && historical.TryGetValue(locationId, out var measured)
            ? measured
            : HandlingMinutes(stop.StopType, loading, unloading);

    private static int HandlingMinutes(StopType stopType, int loading, int unloading) =>
        stopType == StopType.Loading ? loading : unloading;

    private static DateTime? LatestBound(TransportOrderStop stop) =>
        stop.LatestAllowed ?? stop.ConfirmedTo ?? stop.PlannedTo;

    private static EtaStatus ResolveStatus(DateTime eta, DateTime? bound)
    {
        if (bound is not { } b)
        {
            return EtaStatus.OnTime;
        }

        if (eta > b)
        {
            return EtaStatus.Late;
        }

        return eta > b - AtRiskWindow ? EtaStatus.AtRisk : EtaStatus.OnTime;
    }

    /// <summary>History row for every real change; crossing into Late alerts dispatch + customer.</summary>
    private async Task RecordChangeAsync(
        StopEta eta, TransportOrderStop stop, EtaStatus? previousStatus, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _dbContext.Add(new StopEtaHistory
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            StopEtaId = eta.Id,
            Eta = eta.CurrentEta,
            Source = eta.Source,
            Status = eta.Status,
            RecordedAt = now,
            ChangedByUserId = _currentUserContext.CurrentUserId,
        });

        // Wave 8: shift-threshold messaging — a configured tenant threshold also messages the
        // customer when the ETA moves ≥ X minutes while still on time (previous history row =
        // the previous ETA). The existing outside-window messaging below stays unchanged.
        var becameLate = eta.Status == EtaStatus.Late && previousStatus != EtaStatus.Late;
        if (!becameLate)
        {
            var threshold = await _dbContext.TenantSettings.AsNoTracking()
                .Where(s => s.TenantId == _tenantContext.TenantId)
                .Select(s => s.EtaShiftNotifyMinutes)
                .FirstOrDefaultAsync(cancellationToken);
            if (threshold is { } shiftMinutes && previousStatus is not null)
            {
                // The row staged above is unsaved and invisible here — the newest PERSISTED
                // history row is by definition the previous ETA (no time filter: equal
                // timestamps within one second would otherwise hide it).
                var previousEta = await _dbContext.StopEtaHistories.AsNoTracking()
                    .Where(h => h.StopEtaId == eta.Id)
                    .OrderByDescending(h => h.RecordedAt)
                    .Select(h => (DateTime?)h.Eta)
                    .FirstOrDefaultAsync(cancellationToken);
                if (previousEta is { } prior && Math.Abs((eta.CurrentEta - prior).TotalMinutes) >= shiftMinutes)
                {
                    await QueueCustomerEtaMessageAsync(eta, stop, cancellationToken);
                }
            }

            return;
        }

        var stopLabel = stop.LocationName ?? stop.City ?? "stop";
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.PlanningEdit, "eta_changed",
            "ETA overschrijdt het venster",
            $"De verwachte aankomst aan {stopLabel} is nu {eta.CurrentEta:HH:mm} en valt buiten het venster.",
            $"/planning/{eta.TripId}", cancellationToken);

        await QueueCustomerEtaMessageAsync(eta, stop, cancellationToken);
    }

    private async Task QueueCustomerEtaMessageAsync(
        StopEta eta, TransportOrderStop stop, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.Id == stop.TransportOrderId && o.TenantId == _tenantContext.TenantId)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                o => o.CustomerId, c => c.Id, (o, c) => new { o.OrderNumber, CustomerId = c.Id, CustomerName = c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is not null)
        {
            await _messageOutbox.QueueAsync(new MessageRequest(
                MessageKinds.EtaUpdate, MessageOwnerType.Customer, customer.CustomerId,
                new Dictionary<string, string>
                {
                    ["orderNumber"] = customer.OrderNumber,
                    ["customerName"] = customer.CustomerName,
                    ["eta"] = eta.CurrentEta.ToString("dd-MM-yyyy HH:mm"),
                },
                EntityType, eta.Id.ToString(),
                $"eta_update:{stop.Id}:{eta.CurrentEta:yyyyMMddHHmm}"), cancellationToken);
        }
    }

    public async Task NotifyTripStartedAsync(Guid tripId, CancellationToken cancellationToken)
    {
        // Seed the ETAs the moment the wheels start turning — the portal and dispatch see a
        // live expectation instead of waiting for the first manual ETA view.
        var eta = await RecalculateTripAsync(tripId, cancellationToken);
        if (eta is null)
        {
            return;
        }

        var tenantId = _tenantContext.TenantId;
        var orders = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && to.TripId == tripId && !to.IsDeleted)
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == tenantId),
                to => to.TransportOrderId, o => o.Id, (to, o) => new { o.Id, o.OrderNumber, o.CustomerId })
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            var customerName = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == order.CustomerId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
            var firstEta = eta.Stops
                .OrderBy(s => s.CurrentEta)
                .Select(s => (DateTime?)s.CurrentEta)
                .FirstOrDefault();
            await _messageOutbox.QueueAsync(new MessageRequest(
                MessageKinds.DriverEnRoute, MessageOwnerType.Customer, order.CustomerId,
                new Dictionary<string, string>
                {
                    ["orderNumber"] = order.OrderNumber,
                    ["customerName"] = customerName ?? "",
                    ["eta"] = firstEta?.ToString("dd-MM-yyyy HH:mm") ?? "-",
                },
                "Trip", tripId.ToString(),
                // Idempotent per trip+order: restarting a trip never re-mails the customer.
                $"driver_en_route:{tripId}:{order.Id}"), cancellationToken);
        }
    }

    private static StopEtaDto Map(TransportOrderStop stop, StopEta eta) => new(
        stop.Id, stop.LocationName ?? stop.City ?? "stop", stop.StopType,
        eta.CurrentEta, eta.Source, eta.Status, LatestBound(stop), eta.Note);
}
