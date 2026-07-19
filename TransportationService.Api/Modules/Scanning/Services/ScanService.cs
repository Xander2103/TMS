using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Scanning.Services;

public class ScanService : IScanService
{
    private const string EntityType = "ScanEvent";
    private const int HistoryCap = 200;

    /// <summary>Results whose quantities feed the tallies; warnings are recorded but never counted.</summary>
    private static readonly ScanResult[] CountingResults =
        [ScanResult.Expected, ScanResult.OverDelivery, ScanResult.DamagedItem, ScanResult.ManualCorrection];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public ScanService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    public async Task<ScanOperationResult> SubmitAsync(
        Guid tripId, Guid stopId, SubmitScanRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return ScanOperationResult.Invalid("Een barcode is verplicht.");
        }

        if (request.Quantity <= 0)
        {
            return ScanOperationResult.Invalid("De hoeveelheid moet groter dan nul zijn.");
        }

        var guard = await GuardAsync(tripId, stopId, restrictToOwnDriver, requireInProgress: true, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error;
        }

        var stop = guard.Stop!;

        // Scanning stays possible until the stop reaches a terminal status.
        var stopStatus = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TripId == tripId && e.TransportOrderStopId == stopId && e.TenantId == _tenantContext.TenantId)
            .Select(e => (StopExecutionStatus?)e.Status)
            .FirstOrDefaultAsync(cancellationToken) ?? StopExecutionStatus.Planned;
        if (StopStatusMachine.IsTerminal(stopStatus))
        {
            return ScanOperationResult.InvalidState("Deze stop is al afgehandeld; scannen kan niet meer.");
        }

        var barcode = request.Barcode.Trim();
        var barcodeLower = barcode.ToLowerInvariant();

        // Resolve within the trip: the stop's own order wins; other orders on the trip mean
        // a physically present but wrongly scanned item; anything else is unexpected.
        var candidates = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId
                        && guard.OrderIds!.Contains(c.TransportOrderId)
                        && c.Barcode != null && c.Barcode.ToLower() == barcodeLower)
            .ToListAsync(cancellationToken);
        var item = candidates.FirstOrDefault(c => c.TransportOrderId == stop.TransportOrderId)
                   ?? candidates.FirstOrDefault();

        var quantity = request.Quantity;
        decimal priorAccepted = 0;
        ScanResult result;
        if (item is null)
        {
            result = ScanResult.UnexpectedItem;
        }
        else if (item.TransportOrderId != stop.TransportOrderId)
        {
            result = ScanResult.WrongItem;
        }
        else
        {
            priorAccepted = await AcceptedQuantityAsync(tripId, stopId, item.Id, request.ScanType, cancellationToken);
            if (priorAccepted >= item.ExpectedQuantity)
            {
                result = ScanResult.DuplicateScan;
            }
            else if (request.Damaged)
            {
                result = ScanResult.DamagedItem;
            }
            else if (priorAccepted + quantity > item.ExpectedQuantity)
            {
                result = ScanResult.OverDelivery;
            }
            else
            {
                result = ScanResult.Expected;
            }
        }

        var scanEvent = new ScanEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = tripId,
            TransportOrderId = stop.TransportOrderId,
            TransportOrderStopId = stopId,
            CargoItemId = item?.Id,
            UserId = _currentUserContext.CurrentUserId,
            DriverId = guard.DriverId,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            ScanType = request.ScanType,
            Result = result,
            Barcode = barcode,
            Quantity = quantity,
            Damaged = request.Damaged,
            DamageNote = Trim(request.DamageNote),
            DeviceInfo = Trim(request.DeviceInfo),
        };
        _dbContext.Add(scanEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, scanEvent.Id.ToString(), result.ToString(), null,
            new { scanEvent.TripId, scanEvent.TransportOrderStopId, scanEvent.Barcode, scanEvent.Quantity, scanEvent.Result }, cancellationToken);

        var accepted = CountingResults.Contains(result) ? priorAccepted + quantity : priorAccepted;
        var summary = await BuildSummaryAsync(tripId, stop, request.ScanType, cancellationToken);

        return ScanOperationResult.Success(new ScanFeedbackDto(
            scanEvent.Id, result, FeedbackLevel(result), FeedbackMessage(result, item?.Description),
            item?.Id, item?.Description,
            accepted, item?.ExpectedQuantity ?? 0, summary));
    }

    public async Task<ScanOperationResult> CorrectAsync(
        Guid tripId, Guid stopId, ScanCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ScanOperationResult.Invalid("Een reden is verplicht bij een handmatige correctie.");
        }

        if (request.Quantity < 0)
        {
            return ScanOperationResult.Invalid("De gecorrigeerde hoeveelheid kan niet negatief zijn.");
        }

        var guard = await GuardAsync(tripId, stopId, restrictToOwnDriver: false, requireInProgress: false, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error;
        }

        // Corrections repair the record during or after execution, never on unstarted trips.
        if (guard.Trip!.Status is not (TripStatus.InProgress or TripStatus.Completed))
        {
            return ScanOperationResult.InvalidState("Correcties kunnen alleen tijdens of na de uitvoering van de rit.");
        }

        var stop = guard.Stop!;
        var item = await _dbContext.CargoItems.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CargoItemId && c.TenantId == _tenantContext.TenantId
                                      && c.TransportOrderId == stop.TransportOrderId, cancellationToken);
        if (item is null)
        {
            return ScanOperationResult.NotFound;
        }

        var current = await AcceptedQuantityAsync(tripId, stopId, item.Id, request.ScanType, cancellationToken);
        var delta = request.Quantity - current;
        if (delta == 0)
        {
            return ScanOperationResult.Invalid("De geregistreerde hoeveelheid is al gelijk aan de opgegeven waarde.");
        }

        var scanEvent = new ScanEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = tripId,
            TransportOrderId = stop.TransportOrderId,
            TransportOrderStopId = stopId,
            CargoItemId = item.Id,
            UserId = _currentUserContext.CurrentUserId,
            DriverId = guard.DriverId,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            ScanType = request.ScanType,
            Result = ScanResult.ManualCorrection,
            Barcode = item.Barcode,
            Quantity = delta,
            CorrectionReason = request.Reason.Trim(),
        };
        _dbContext.Add(scanEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, scanEvent.Id.ToString(), "ManualCorrection",
            new { Quantity = current },
            new { scanEvent.TripId, scanEvent.TransportOrderStopId, scanEvent.CargoItemId, Quantity = request.Quantity, scanEvent.CorrectionReason },
            cancellationToken);

        var summary = await BuildSummaryAsync(tripId, stop, request.ScanType, cancellationToken);

        return ScanOperationResult.Success(new ScanFeedbackDto(
            scanEvent.Id, ScanResult.ManualCorrection, ScanFeedbackLevel.Success,
            FeedbackMessage(ScanResult.ManualCorrection, item.Description),
            item.Id, item.Description,
            request.Quantity, item.ExpectedQuantity, summary));
    }

    public async Task<ScanHistoryResult> ListAsync(
        Guid tripId, Guid? stopId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var guard = await GuardAsync(tripId, stopId: null, restrictToOwnDriver, requireInProgress: false, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error.Outcome == ScanOutcome.NotYourTrip ? ScanHistoryResult.NotYourTrip : ScanHistoryResult.NotFound;
        }

        var query = _dbContext.ScanEvents.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId);
        if (stopId is { } s)
        {
            query = query.Where(e => e.TransportOrderStopId == s);
        }

        var rows = await query
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.CreatedAt)
            .Take(HistoryCap)
            .GroupJoin(_dbContext.Users.AsNoTracking().Where(u => u.TenantId == _tenantContext.TenantId),
                e => e.UserId, u => u.Id, (e, users) => new { Event = e, Users = users })
            .SelectMany(x => x.Users.DefaultIfEmpty(), (x, u) => new { x.Event, UserName = u == null ? null : u.FirstName + " " + u.LastName })
            .GroupJoin(_dbContext.CargoItems.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                x => x.Event.CargoItemId, c => c.Id, (x, items) => new { x.Event, x.UserName, Items = items })
            .SelectMany(x => x.Items.DefaultIfEmpty(), (x, c) => new { x.Event, x.UserName, Description = c == null ? null : c.Description })
            .ToListAsync(cancellationToken);

        return ScanHistoryResult.Success(rows
            .Select(x => new ScanEventDto(
                x.Event.Id, x.Event.TransportOrderStopId, x.Event.CargoItemId, x.Description,
                x.Event.ScanType, x.Event.Result, x.Event.Barcode, x.Event.Quantity,
                x.Event.Damaged, x.Event.DamageNote, x.Event.CorrectionReason, x.Event.DeviceInfo,
                x.UserName, x.Event.OccurredAt))
            .ToList());
    }

    public async Task<ScanSummaryResult> GetStopSummaryAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var guard = await GuardAsync(tripId, stopId, restrictToOwnDriver, requireInProgress: false, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error.Outcome == ScanOutcome.NotYourTrip ? ScanSummaryResult.NotYourTrip : ScanSummaryResult.NotFound;
        }

        var stop = guard.Stop!;
        var scanType = stop.StopType == StopType.Loading ? ScanType.Load : ScanType.Unload;
        return ScanSummaryResult.Success(await BuildSummaryAsync(tripId, stop, scanType, cancellationToken));
    }

    private sealed record GuardResult(
        Trip? Trip, TransportOrderStop? Stop, List<Guid>? OrderIds, Guid? DriverId, ScanOperationResult? Error);

    private async Task<GuardResult> GuardAsync(
        Guid tripId, Guid? stopId, bool restrictToOwnDriver, bool requireInProgress, CancellationToken cancellationToken)
    {
        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return new GuardResult(null, null, null, null, ScanOperationResult.NotFound);
        }

        Guid? driverId = null;
        if (_currentUserContext.CurrentUserId is { } userId)
        {
            driverId = await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.EmployeeId != null)
                .Join(_dbContext.Drivers.AsNoTracking().Where(d => d.TenantId == _tenantContext.TenantId),
                    u => u.EmployeeId, d => d.EmployeeId, (u, d) => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (restrictToOwnDriver && (driverId is null || trip.DriverId != driverId))
        {
            return new GuardResult(null, null, null, null, ScanOperationResult.NotYourTrip);
        }

        if (requireInProgress && trip.Status != TripStatus.InProgress)
        {
            return new GuardResult(null, null, null, null,
                ScanOperationResult.InvalidState("Scannen kan alleen terwijl de rit onderweg is."));
        }

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();

        TransportOrderStop? stop = null;
        if (stopId is { } sid)
        {
            stop = await _dbContext.TransportOrderStops.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sid && s.TenantId == _tenantContext.TenantId
                                          && orderIds.Contains(s.TransportOrderId), cancellationToken);
            if (stop is null)
            {
                return new GuardResult(null, null, null, null, ScanOperationResult.NotFound);
            }
        }

        return new GuardResult(trip, stop, orderIds, driverId, null);
    }

    private async Task<decimal> AcceptedQuantityAsync(
        Guid tripId, Guid stopId, Guid cargoItemId, ScanType scanType, CancellationToken cancellationToken)
    {
        var quantities = await _dbContext.ScanEvents.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId
                        && e.TransportOrderStopId == stopId && e.CargoItemId == cargoItemId
                        && e.ScanType == scanType && CountingResults.Contains(e.Result))
            .Select(e => e.Quantity)
            .ToListAsync(cancellationToken);
        return quantities.Sum();
    }

    private async Task<StopScanSummaryDto> BuildSummaryAsync(
        Guid tripId, TransportOrderStop stop, ScanType scanType, CancellationToken cancellationToken)
    {
        var items = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == stop.TransportOrderId)
            .OrderBy(c => c.Sequence)
            .ToListAsync(cancellationToken);

        var events = await _dbContext.ScanEvents.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId
                        && e.TransportOrderStopId == stop.Id && e.ScanType == scanType)
            .ToListAsync(cancellationToken);

        var summaries = items.Select(item =>
        {
            var itemEvents = events.Where(e => e.CargoItemId == item.Id && CountingResults.Contains(e.Result)).ToList();
            var scanned = itemEvents.Sum(e => e.Quantity);
            var damaged = itemEvents.Where(e => e.Damaged).Sum(e => e.Quantity);
            var state = scanned == 0 ? CargoScanState.Missing
                : scanned < item.ExpectedQuantity ? CargoScanState.Partial
                : scanned == item.ExpectedQuantity ? CargoScanState.Complete
                : CargoScanState.Over;
            return new CargoItemScanSummaryDto(
                item.Id, item.Sequence, item.Description, item.Barcode,
                item.ExpectedQuantity, item.QuantityUnit, scanned, damaged, state);
        }).ToList();

        return new StopScanSummaryDto(
            stop.Id, scanType, summaries,
            events.Count(e => e.Result == ScanResult.UnexpectedItem),
            events.Count);
    }

    private static ScanFeedbackLevel FeedbackLevel(ScanResult result) => result switch
    {
        ScanResult.Expected or ScanResult.ManualCorrection => ScanFeedbackLevel.Success,
        _ => ScanFeedbackLevel.Warning,
    };

    private static string FeedbackMessage(ScanResult result, string? description) => result switch
    {
        ScanResult.Expected => $"{description ?? "Item"} geregistreerd.",
        ScanResult.UnexpectedItem => "Onbekende barcode voor deze rit — gemeld aan planning.",
        ScanResult.WrongItem => $"{description ?? "Item"} hoort bij een andere opdracht op deze rit.",
        ScanResult.DuplicateScan => $"{description ?? "Item"} was al volledig gescand; duplicaat geregistreerd.",
        ScanResult.OverDelivery => $"Meer gescand dan verwacht voor {description ?? "dit item"}.",
        ScanResult.DamagedItem => $"Schade geregistreerd voor {description ?? "dit item"}.",
        ScanResult.ManualCorrection => $"Hoeveelheid van {description ?? "item"} gecorrigeerd.",
        _ => "Scan geregistreerd.",
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
