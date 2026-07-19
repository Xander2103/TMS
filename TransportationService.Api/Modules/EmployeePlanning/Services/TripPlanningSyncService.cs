using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

public class TripPlanningSyncService : ITripPlanningSyncService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public TripPlanningSyncService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<TripPlanningSyncResult> ApplyAsync(Trip trip, CancellationToken cancellationToken)
    {
        var entry = await FindEntryAsync(trip.Id, cancellationToken);

        if (trip.DriverId is not { } driverId)
        {
            return SoftDelete(entry);
        }

        var employeeId = await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.Id == driverId && d.TenantId == _tenantContext.TenantId)
            .Select(d => (Guid?)d.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
        if (employeeId is null)
        {
            // Driver reference vanished (soft-deleted) — treat as unassigned.
            return SoftDelete(entry);
        }

        var vehicleSummary = trip.VehicleId is { } vehicleId
            ? await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId)
                .Select(v => v.InternalNumber + " · " + v.LicensePlate)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var routeSummary = await BuildRouteSummaryAsync(trip, cancellationToken);

        if (entry is null)
        {
            entry = new TripPlanningEntry
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TripId = trip.Id,
            };
            _dbContext.Add(entry);
            Project(entry, trip, driverId, employeeId.Value, vehicleSummary, routeSummary);
            return new TripPlanningSyncResult(TripPlanningSyncAction.Created, entry.Id, null, employeeId, entry);
        }

        var previousEmployeeId = entry.EmployeeId;
        var wasDeleted = entry.IsDeleted;
        if (wasDeleted)
        {
            // A driver was re-assigned after removal: the filtered unique index owns (TenantId, TripId),
            // so the original row is resurrected instead of inserting a competitor.
            entry.IsDeleted = false;
            entry.DeletedAt = null;
            entry.DeletedByUserId = null;
        }

        Project(entry, trip, driverId, employeeId.Value, vehicleSummary, routeSummary);

        var action = trip.Status == TripStatus.Cancelled
            ? TripPlanningSyncAction.Cancelled
            : wasDeleted
                ? TripPlanningSyncAction.Created
                : previousEmployeeId != employeeId
                    ? TripPlanningSyncAction.Moved
                    : TripPlanningSyncAction.Updated;

        return new TripPlanningSyncResult(action, entry.Id,
            previousEmployeeId == employeeId ? null : previousEmployeeId, employeeId, entry);
    }

    public async Task<TripPlanningSyncResult> ApplyActualsAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var entry = await FindEntryAsync(tripId, cancellationToken);
        if (entry is null || entry.IsDeleted)
        {
            return TripPlanningSyncResult.None;
        }

        var executions = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TripId == tripId && e.TenantId == _tenantContext.TenantId)
            .Select(e => new { e.ArrivedAt, e.CompletedAt, e.DepartedAt })
            .ToListAsync(cancellationToken);

        var start = executions.Where(e => e.ArrivedAt != null).Min(e => e.ArrivedAt);
        var end = executions
            .Select(e => e.DepartedAt ?? e.CompletedAt)
            .Where(t => t != null)
            .DefaultIfEmpty(null)
            .Max();

        if (entry.ActualStart == start && entry.ActualEnd == end)
        {
            return TripPlanningSyncResult.None;
        }

        entry.ActualStart = start;
        entry.ActualEnd = end;
        return new TripPlanningSyncResult(TripPlanningSyncAction.Updated, entry.Id, null, entry.EmployeeId, entry);
    }

    public async Task<TripPlanningSyncResult> ApplyRemovalAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var entry = await FindEntryAsync(tripId, cancellationToken);
        return SoftDelete(entry);
    }

    private static void Project(
        TripPlanningEntry entry, Trip trip, Guid driverId, Guid employeeId,
        string? vehicleSummary, string? routeSummary)
    {
        entry.EmployeeId = employeeId;
        entry.DriverId = driverId;
        entry.TripNumber = trip.TripNumber;
        entry.Date = trip.TripDate;
        entry.PlannedStart = trip.PlannedStart;
        entry.PlannedEnd = trip.PlannedEnd;
        entry.VehicleSummary = vehicleSummary;
        entry.RouteSummary = routeSummary;
        entry.Status = trip.Status;
        entry.Notes = trip.Notes;
    }

    private TripPlanningSyncResult SoftDelete(TripPlanningEntry? entry)
    {
        if (entry is null || entry.IsDeleted)
        {
            return TripPlanningSyncResult.None;
        }

        _dbContext.Remove(entry); // soft delete via interceptor
        return new TripPlanningSyncResult(TripPlanningSyncAction.Removed, entry.Id, entry.EmployeeId, null, entry);
    }

    /// <summary>Soft-deleted rows included: re-assignment must resurrect, not duplicate.</summary>
    private Task<TripPlanningEntry?> FindEntryAsync(Guid tripId, CancellationToken cancellationToken) =>
        _dbContext.TripPlanningEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId, cancellationToken);

    private async Task<string?> BuildRouteSummaryAsync(Trip trip, CancellationToken cancellationToken)
    {
        var orderIds = trip.Orders
            .Where(o => !o.IsDeleted)
            .OrderBy(o => o.Sequence)
            .Select(o => o.TransportOrderId)
            .ToList();
        if (orderIds.Count == 0)
        {
            return null;
        }

        var stops = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && orderIds.Contains(s.TransportOrderId))
            .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == _tenantContext.TenantId),
                s => s.LocationId, l => l.Id,
                (s, locations) => new { Stop = s, Locations = locations })
            .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new
            {
                x.Stop.TransportOrderId,
                x.Stop.Sequence,
                x.Stop.StopType,
                City = x.Stop.City ?? (l != null ? l.City : null),
            })
            .ToListAsync(cancellationToken);

        var ordered = orderIds
            .SelectMany(orderId => stops
                .Where(s => s.TransportOrderId == orderId)
                .OrderBy(s => s.Sequence))
            .ToList();

        var firstLoading = ordered.FirstOrDefault(s => s.StopType == StopType.Loading && !string.IsNullOrWhiteSpace(s.City))?.City;
        var lastUnloading = ordered.LastOrDefault(s => s.StopType == StopType.Unloading && !string.IsNullOrWhiteSpace(s.City))?.City;

        return (firstLoading, lastUnloading) switch
        {
            (null, null) => null,
            (var f, null) => f,
            (null, var l) => l,
            var (f, l) => $"{f} → {l}",
        };
    }
}
