using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

public interface ITripPackageService
{
    Task<TripPackageOperationResult> GetChecklistAsync(
        Guid tripId, Guid? stopId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<TripPackageOperationResult> GetReadinessAsync(
        Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Readiness for a trip entity already loaded by the caller (the departure gate path).</summary>
    Task<TripPackageReadinessDto> EvaluateReadinessAsync(Trip trip, CancellationToken cancellationToken);

    /// <summary>
    /// STAGES a DepartureOverride custody event on every outstanding mandatory package
    /// (never saves) — the trip status change and the override trail commit atomically.
    /// </summary>
    Task StageDepartureOverrideAsync(Trip trip, string? reason, CancellationToken cancellationToken);

    Task<TripPackageOperationResult> MarkMissingAsync(
        Guid tripId, Guid packageId, MarkPackageMissingRequest request, bool restrictToOwnDriver,
        CancellationToken cancellationToken);

    Task<TripPackageOperationResult> ResolveIncidentAsync(
        Guid packageId, ResolvePackageIncidentRequest request, CancellationToken cancellationToken);
}

public class TripPackageService : ITripPackageService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IPackageEventWriter _eventWriter;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public TripPackageService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IPackageEventWriter eventWriter,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _eventWriter = eventWriter;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    public async Task<TripPackageOperationResult> GetChecklistAsync(
        Guid tripId, Guid? stopId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var guard = await GuardAsync(tripId, restrictToOwnDriver, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error;
        }

        var checklist = await BuildChecklistAsync(guard.Trip!, cancellationToken);
        if (stopId is { } s)
        {
            checklist = checklist with { Stops = checklist.Stops.Where(x => x.StopId == s).ToList() };
        }
        return TripPackageOperationResult.Success(checklist);
    }

    public async Task<TripPackageOperationResult> GetReadinessAsync(
        Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var guard = await GuardAsync(tripId, restrictToOwnDriver, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error;
        }

        return TripPackageOperationResult.Success(await EvaluateReadinessAsync(guard.Trip!, cancellationToken));
    }

    public async Task<TripPackageReadinessDto> EvaluateReadinessAsync(Trip trip, CancellationToken cancellationToken)
    {
        var rule = await GetRuleAsync(cancellationToken);
        var packages = await LoadTripPackagesAsync(trip, cancellationToken);

        // Group parents mirror their children; only leaves count, or totals double.
        var parentIds = packages.Where(p => p.Package.ParentPackageId is not null)
            .Select(p => p.Package.ParentPackageId!.Value)
            .ToHashSet();
        var leaves = packages
            .Where(p => !parentIds.Contains(p.Package.Id) && p.Package.CurrentLifecycleStatus != PackageLifecycleStatus.Cancelled)
            .ToList();
        var mandatory = leaves.Where(p => p.Package.IsMandatory).ToList();

        var loaded = mandatory.Count(p => IsOnVehicle(p.Package.CurrentLifecycleStatus));
        var missing = mandatory.Count(p => p.Package.CurrentLifecycleStatus == PackageLifecycleStatus.Missing);
        var damaged = mandatory.Count(p => p.Package.CurrentLifecycleStatus == PackageLifecycleStatus.Damaged);
        var outstanding = mandatory
            .Where(p => !IsOnVehicle(p.Package.CurrentLifecycleStatus))
            .Select(p => ToItem(p, parentIds))
            .ToList();

        var openExceptions = await _dbContext.ExecutionExceptions.AsNoTracking()
            .CountAsync(e => e.TenantId == _tenantContext.TenantId && e.TripId == trip.Id && e.PackageId != null
                             && (e.Status == ExecutionExceptionStatus.Open || e.Status == ExecutionExceptionStatus.Investigating),
                cancellationToken);

        var isComplete = outstanding.Count == 0;
        return new TripPackageReadinessDto(
            trip.Id, rule,
            TotalPackages: leaves.Count,
            MandatoryPackages: mandatory.Count,
            LoadedCount: loaded,
            NotLoadedCount: outstanding.Count,
            MissingCount: missing,
            DamagedCount: damaged,
            OpenExceptionCount: openExceptions,
            IsComplete: isComplete,
            RequiresOverride: !isComplete && rule == PackageDepartureRule.RequireOverride,
            IsBlocked: !isComplete && rule == PackageDepartureRule.Block,
            OutstandingPackages: outstanding);
    }

    public async Task StageDepartureOverrideAsync(Trip trip, string? reason, CancellationToken cancellationToken)
    {
        var readiness = await EvaluateReadinessAsync(trip, cancellationToken);
        var outstandingIds = readiness.OutstandingPackages.Select(p => p.PackageId).ToList();
        if (outstandingIds.Count == 0)
        {
            return;
        }

        var packages = await _dbContext.Packages
            .Where(p => p.TenantId == _tenantContext.TenantId && outstandingIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        foreach (var package in packages)
        {
            _eventWriter.Stage(package, PackageEventType.DepartureOverride,
                package.CurrentLifecycleStatus, package.CurrentLifecycleStatus,
                new PackageEventContext(TripId: trip.Id, IsOverride: true,
                    Notes: string.IsNullOrWhiteSpace(reason) ? "Vertrek vrijgegeven zonder volledige lading." : reason.Trim()));
        }

        await _auditService.RecordAsync("Trip", trip.Id.ToString(), "PackageDepartureOverride", null,
            new { trip.TripNumber, Outstanding = outstandingIds.Count, Reason = reason }, cancellationToken);
    }

    public async Task<TripPackageOperationResult> MarkMissingAsync(
        Guid tripId, Guid packageId, MarkPackageMissingRequest request, bool restrictToOwnDriver,
        CancellationToken cancellationToken)
    {
        var guard = await GuardAsync(tripId, restrictToOwnDriver, cancellationToken);
        if (guard.Error is not null)
        {
            return guard.Error;
        }

        var trip = guard.Trip!;
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        var package = await _dbContext.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (package is null || !orderIds.Contains(package.TransportOrderId))
        {
            return TripPackageOperationResult.NotFound;
        }

        if (!PackageLifecycleMachine.IsAllowed(package.CurrentLifecycleStatus, PackageLifecycleStatus.Missing))
        {
            return TripPackageOperationResult.InvalidState(
                $"Colli {package.PackageNumber} kan in status '{package.CurrentLifecycleStatus}' niet als vermist worden gemeld.");
        }

        var hasChildren = await _dbContext.Packages.AsNoTracking()
            .AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.ParentPackageId == package.Id, cancellationToken);
        if (hasChildren)
        {
            return TripPackageOperationResult.Invalid(
                "Meld de onderliggende colli individueel als vermist; een groep kan niet in één keer vermist zijn.");
        }

        var old = package.CurrentLifecycleStatus;
        package.CurrentLifecycleStatus = PackageLifecycleStatus.Missing;
        package.CurrentExceptionStatus = PackageExceptionState.Open;

        var exception = new ExecutionException
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = trip.Id,
            TransportOrderId = package.TransportOrderId,
            TransportOrderStopId = request.StopId == Guid.Empty ? null : request.StopId,
            PackageId = package.Id,
            Type = ExecutionExceptionType.MissingPackage,
            Severity = ExceptionSeverity.High,
            Status = ExecutionExceptionStatus.Open,
            Description = $"Colli {package.PackageNumber} niet aangetroffen bij het laden."
                          + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" {request.Note.Trim()}"),
            ReportedByUserId = _currentUserContext.CurrentUserId,
            DriverId = guard.DriverId,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _dbContext.Add(exception);

        _eventWriter.Stage(package, PackageEventType.MarkedMissing, old, PackageLifecycleStatus.Missing,
            new PackageEventContext(TripId: trip.Id,
                StopId: request.StopId == Guid.Empty ? null : request.StopId,
                DriverId: guard.DriverId, ExceptionId: exception.Id,
                Notes: string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("Package", package.Id.ToString(), "MarkedMissing",
            new { OldStatus = old.ToString() },
            new { package.PackageNumber, TripId = trip.Id, request.Note }, cancellationToken);

        return TripPackageOperationResult.Success(new { ExceptionId = exception.Id });
    }

    public async Task<TripPackageOperationResult> ResolveIncidentAsync(
        Guid packageId, ResolvePackageIncidentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return TripPackageOperationResult.Invalid("Een toelichting is verplicht bij het oplossen van een incident.");
        }

        var package = await _dbContext.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (package is null)
        {
            return TripPackageOperationResult.NotFound;
        }

        var target = request.Action switch
        {
            PackageIncidentAction.Found => PackageLifecycleStatus.AwaitingLoading,
            PackageIncidentAction.ReleaseToLoad => PackageLifecycleStatus.AwaitingLoading,
            PackageIncidentAction.Return => PackageLifecycleStatus.ReturnPending,
            PackageIncidentAction.Quarantine => PackageLifecycleStatus.Quarantined,
            PackageIncidentAction.Cancel => PackageLifecycleStatus.Cancelled,
            _ => (PackageLifecycleStatus?)null,
        };
        if (target is null)
        {
            return TripPackageOperationResult.Invalid("Onbekende actie.");
        }

        // Actions are status-specific: Found repairs Missing, ReleaseToLoad repairs Damaged.
        if (request.Action == PackageIncidentAction.Found && package.CurrentLifecycleStatus != PackageLifecycleStatus.Missing)
        {
            return TripPackageOperationResult.InvalidState("Alleen een vermist colli kan als gevonden worden gemeld.");
        }
        if (request.Action == PackageIncidentAction.ReleaseToLoad && package.CurrentLifecycleStatus != PackageLifecycleStatus.Damaged)
        {
            return TripPackageOperationResult.InvalidState("Alleen een beschadigd colli kan worden vrijgegeven om te laden.");
        }

        if (!PackageLifecycleMachine.IsAllowed(package.CurrentLifecycleStatus, target.Value))
        {
            return TripPackageOperationResult.InvalidState(
                $"Actie '{request.Action}' is niet mogelijk vanuit status '{package.CurrentLifecycleStatus}'.");
        }

        var old = package.CurrentLifecycleStatus;
        package.CurrentLifecycleStatus = target.Value;

        // Resolve the open package exceptions this action addresses; the trail stays append-only.
        var openExceptions = await _dbContext.ExecutionExceptions
            .Where(e => e.TenantId == _tenantContext.TenantId && e.PackageId == package.Id
                        && (e.Status == ExecutionExceptionStatus.Open || e.Status == ExecutionExceptionStatus.Investigating))
            .ToListAsync(cancellationToken);
        var toResolve = request.Action switch
        {
            PackageIncidentAction.Found => openExceptions.Where(e => e.Type == ExecutionExceptionType.MissingPackage).ToList(),
            PackageIncidentAction.ReleaseToLoad => openExceptions.Where(e => e.Type == ExecutionExceptionType.DamagedPackage).ToList(),
            _ => openExceptions,
        };
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var exception in toResolve)
        {
            exception.Status = ExecutionExceptionStatus.Resolved;
            exception.ResolutionNote = request.Note.Trim();
            exception.ResolvedByUserId = _currentUserContext.CurrentUserId;
            exception.ResolvedAt = now;
        }

        var stillOpen = openExceptions.Count > toResolve.Count;
        package.CurrentExceptionStatus = stillOpen
            ? PackageExceptionState.Open
            : openExceptions.Count > 0 ? PackageExceptionState.Resolved : package.CurrentExceptionStatus;

        var eventType = request.Action == PackageIncidentAction.Cancel ? PackageEventType.Cancelled
            : request.Action == PackageIncidentAction.Quarantine ? PackageEventType.Quarantined
            : PackageEventType.ExceptionResolved;
        _eventWriter.Stage(package, eventType, old, target.Value,
            new PackageEventContext(Result: request.Action.ToString(), Notes: request.Note.Trim(),
                ExceptionId: toResolve.FirstOrDefault()?.Id));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("Package", package.Id.ToString(), $"Incident{request.Action}",
            new { OldStatus = old.ToString() },
            new { package.PackageNumber, NewStatus = target.Value.ToString(), request.Note, Resolved = toResolve.Count },
            cancellationToken);

        return TripPackageOperationResult.Success();
    }

    private async Task<TripPackageChecklistDto> BuildChecklistAsync(Trip trip, CancellationToken cancellationToken)
    {
        var packages = await LoadTripPackagesAsync(trip, cancellationToken);
        var parentIds = packages.Where(p => p.Package.ParentPackageId is not null)
            .Select(p => p.Package.ParentPackageId!.Value)
            .ToHashSet();

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        var stops = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && orderIds.Contains(s.TransportOrderId))
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        var stopLists = new List<TripPackageStopChecklistDto>();
        foreach (var stop in stops)
        {
            var isLoading = stop.StopType == StopType.Loading;
            var defaultStopId = ResolveDefaultStop(stops, stop.TransportOrderId, isLoading);
            var expected = packages
                .Where(p => p.Package.TransportOrderId == stop.TransportOrderId)
                .Where(p =>
                {
                    var pinned = isLoading ? p.Package.LoadingStopId : p.Package.DeliveryStopId;
                    return pinned == stop.Id || (pinned is null && stop.Id == defaultStopId);
                })
                .Select(p => ToItem(p, parentIds))
                .OrderBy(i => i.PackageNumber)
                .ToList();

            stopLists.Add(new TripPackageStopChecklistDto(
                stop.Id, stop.TransportOrderId, stop.StopType.ToString(), stop.Sequence,
                stop.LocationName, stop.City, expected));
        }

        return new TripPackageChecklistDto(trip.Id, stopLists);
    }

    /// <summary>Unpinned packages default to the first loading / last unloading stop of their order.</summary>
    private static Guid? ResolveDefaultStop(List<TransportOrderStop> stops, Guid orderId, bool loading)
    {
        var ordered = stops.Where(s => s.TransportOrderId == orderId).OrderBy(s => s.Sequence).ToList();
        return loading
            ? ordered.FirstOrDefault(s => s.StopType == StopType.Loading)?.Id
            : ordered.LastOrDefault(s => s.StopType == StopType.Unloading)?.Id;
    }

    private sealed record PackageRow(Package Package, string OrderNumber);

    private async Task<List<PackageRow>> LoadTripPackagesAsync(Trip trip, CancellationToken cancellationToken)
    {
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        if (orderIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Packages.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId && orderIds.Contains(p.TransportOrderId))
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == _tenantContext.TenantId),
                p => p.TransportOrderId, o => o.Id, (p, o) => new PackageRow(p, o.OrderNumber))
            .ToListAsync(cancellationToken);
    }

    private static TripPackageChecklistItemDto ToItem(PackageRow row, HashSet<Guid> parentIds) => new(
        row.Package.Id, row.Package.PackageNumber, row.Package.Description,
        row.Package.Quantity, row.Package.UnitType.ToString(), row.Package.UnitTypeLabel,
        row.Package.CurrentLifecycleStatus, row.Package.CurrentExceptionStatus,
        row.Package.IsMandatory, parentIds.Contains(row.Package.Id), row.Package.ParentPackageId,
        row.Package.BarcodeValue, row.Package.TransportOrderId, row.OrderNumber);

    private static bool IsOnVehicle(PackageLifecycleStatus status) =>
        status is PackageLifecycleStatus.Loaded or PackageLifecycleStatus.InTransit or PackageLifecycleStatus.AtStop
            or PackageLifecycleStatus.Delivered or PackageLifecycleStatus.PartiallyDelivered;

    private async Task<PackageDepartureRule> GetRuleAsync(CancellationToken cancellationToken)
    {
        var raw = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => s.PackageDepartureRule)
            .FirstOrDefaultAsync(cancellationToken);
        return Enum.TryParse<PackageDepartureRule>(raw, out var rule) ? rule : PackageDepartureRule.AllowWithWarning;
    }

    private sealed record GuardResult(Trip? Trip, Guid? DriverId, TripPackageOperationResult? Error);

    private async Task<GuardResult> GuardAsync(Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return new GuardResult(null, null, TripPackageOperationResult.NotFound);
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
            return new GuardResult(null, null, TripPackageOperationResult.NotYourTrip);
        }

        return new GuardResult(trip, driverId, null);
    }
}
