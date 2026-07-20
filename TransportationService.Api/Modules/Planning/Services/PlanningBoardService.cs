using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

public class PlanningBoardService : IPlanningBoardService
{
    private const int MaxRangeDays = 31;
    private const int DefaultExpiryWarningDays = 30;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPlanningConflictService _conflictService;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;

    public PlanningBoardService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IPlanningConflictService conflictService,
        IQualificationStatusCalculator statusCalculator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _conflictService = conflictService;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider;
    }

    private (DateOnly From, DateOnly To) NormalizeRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var start = from ?? today;
        var end = to ?? start;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            end = start.AddDays(MaxRangeDays - 1);
        }

        return (start, end);
    }

    // -----------------------------------------------------------------------
    // Board
    // -----------------------------------------------------------------------

    public async Task<PlanningBoardDto> GetBoardAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var (start, end) = NormalizeRange(from, to);

        var trips = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .Where(t => t.TenantId == tenantId && t.TripDate >= start && t.TripDate <= end)
            .OrderBy(t => t.TripDate).ThenBy(t => t.PlannedStart).ThenBy(t => t.TripNumber)
            .Take(500)
            .ToListAsync(cancellationToken);

        var orderIds = trips.SelectMany(t => t.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId))
            .Distinct().ToList();

        // Batched context: order cargo totals, stop cities/counts, resource names, capacities.
        var orderTotals = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.WeightKg, o.VolumeM3 })
                .ToDictionaryAsync(o => o.Id, cancellationToken);
        var cargoLines = orderIds.Count == 0
            ? []
            : await _dbContext.CargoItems.AsNoTracking()
                .Where(c => c.TenantId == tenantId && orderIds.Contains(c.TransportOrderId))
                .Select(c => new { c.TransportOrderId, c.TotalWeightKg, c.WeightPerUnitKg, c.ExpectedQuantity, c.VolumeM3 })
                .ToListAsync(cancellationToken);
        var cargoByOrder = cargoLines.ToLookup(c => c.TransportOrderId);

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

        var driverIds = trips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var vehicleIds = trips.Where(t => t.VehicleId != null).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var trailerIds = trips.Where(t => t.TrailerId != null).Select(t => t.TrailerId!.Value).Distinct().ToList();

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
                .Select(v => new { v.Id, v.InternalNumber, v.PayloadKg, v.VolumeM3 })
                .ToDictionaryAsync(v => v.Id, cancellationToken);
        var trailers = trailerIds.Count == 0
            ? []
            : await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && trailerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.InternalNumber, t.CapacityKg, t.VolumeM3 })
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        var items = new List<PlanningBoardTripDto>(trips.Count);
        foreach (var trip in trips)
        {
            var tripOrderIds = trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence)
                .Select(o => o.TransportOrderId).ToList();

            decimal weight = 0, volume = 0;
            var hasWeight = false;
            var hasVolume = false;
            foreach (var orderId in tripOrderIds)
            {
                var lines = cargoByOrder[orderId].ToList();
                var lineWeights = lines.Select(l => l.TotalWeightKg ?? l.WeightPerUnitKg * l.ExpectedQuantity)
                    .Where(w => w is not null).Sum(w => w!.Value);
                var orderWeight = lineWeights > 0 ? lineWeights : orderTotals.GetValueOrDefault(orderId)?.WeightKg;
                if (orderWeight is { } w) { weight += w; hasWeight = true; }

                var lineVolumes = lines.Select(l => l.VolumeM3 * l.ExpectedQuantity)
                    .Where(v => v is not null).Sum(v => v!.Value);
                var orderVolume = lineVolumes > 0 ? lineVolumes : orderTotals.GetValueOrDefault(orderId)?.VolumeM3;
                if (orderVolume is { } v2) { volume += v2; hasVolume = true; }
            }

            var tripStops = tripOrderIds.SelectMany(id => stopsByOrder[id]).ToList();
            var firstLoad = tripOrderIds
                .SelectMany(id => stopsByOrder[id].Where(s => s.StopType == StopType.Loading).OrderBy(s => s.Sequence))
                .FirstOrDefault();
            var lastUnload = tripOrderIds
                .SelectMany(id => stopsByOrder[id].Where(s => s.StopType == StopType.Unloading).OrderBy(s => s.Sequence))
                .LastOrDefault();
            var route = firstLoad?.City is null && lastUnload?.City is null
                ? null
                : $"{firstLoad?.City ?? "?"} → {lastUnload?.City ?? "?"}";

            // Capacity source mirrors the conflict engine: trailer when assigned, else vehicle.
            decimal? capacityWeight = null, capacityVolume = null;
            if (trip.TrailerId is { } trailerId && trailers.TryGetValue(trailerId, out var trailer))
            {
                capacityWeight = trailer.CapacityKg;
                capacityVolume = trailer.VolumeM3;
            }
            else if (trip.VehicleId is { } vehicleId && vehicles.TryGetValue(vehicleId, out var vehicle))
            {
                capacityWeight = vehicle.PayloadKg;
                capacityVolume = vehicle.VolumeM3;
            }

            var conflicts = await _conflictService.EvaluateAsync(trip, cancellationToken);

            items.Add(new PlanningBoardTripDto(
                trip.Id, trip.TripNumber, trip.TripDate, trip.Status, trip.PlannedStart, trip.PlannedEnd,
                trip.DriverId, trip.DriverId is { } d ? drivers.GetValueOrDefault(d) : null,
                trip.VehicleId, trip.VehicleId is { } v ? vehicles.GetValueOrDefault(v)?.InternalNumber : null,
                trip.TrailerId, trip.TrailerId is { } tr ? trailers.GetValueOrDefault(tr)?.InternalNumber : null,
                tripOrderIds.Count, tripStops.Count, route,
                hasWeight ? weight : null, hasVolume ? volume : null,
                capacityWeight, capacityVolume,
                conflicts.Count(c => c.Severity == Common.Scheduling.ConflictSeverity.Blocking),
                conflicts.Count(c => c.Severity == Common.Scheduling.ConflictSeverity.Warning),
                trip.Version));
        }

        return new PlanningBoardDto(start, end, items);
    }

    // -----------------------------------------------------------------------
    // Unplanned work
    // -----------------------------------------------------------------------

    public async Task<PagedResult<UnplannedOrderDto>> GetUnplannedOrdersAsync(
        UnplannedOrdersQuery query, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var page = PageRequest.Of(query.Page, Math.Min(query.PageSize, 100));

        // Unplanned = ready for planning (Confirmed) or awaiting review (Submitted), and not
        // already claimed by a non-cancelled trip.
        var orders = _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId
                        && (o.Status == TransportOrderStatus.Confirmed || o.Status == TransportOrderStatus.Submitted)
                        && !_dbContext.TripOrders.Any(to =>
                            to.TenantId == tenantId && to.TransportOrderId == o.Id
                            && _dbContext.Trips.Any(t => t.Id == to.TripId && t.Status != TripStatus.Cancelled)));

        if (query.Status is { } status) orders = orders.Where(o => o.Status == status);
        if (query.Priority is { } priority) orders = orders.Where(o => o.Priority == priority);
        if (query.CustomerId is { } customerId) orders = orders.Where(o => o.CustomerId == customerId);
        if (query.FromDate is { } fromDate) orders = orders.Where(o => o.OrderDate >= fromDate);
        if (query.ToDate is { } toDate) orders = orders.Where(o => o.OrderDate <= toDate);

        var joined = from o in orders
                     join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                         on o.CustomerId equals c.Id
                     select new { o, CustomerName = c.Name };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            joined = joined.Where(x =>
                x.o.OrderNumber.ToLower().Contains(term)
                || x.CustomerName.ToLower().Contains(term)
                || (x.o.CustomerReference != null && x.o.CustomerReference.ToLower().Contains(term))
                || (x.o.GoodsDescription != null && x.o.GoodsDescription.ToLower().Contains(term)));
        }

        // The attention filter needs derived badges; apply it after materialising the page
        // would break paging, so it works on the queryable's simple conditions instead.
        if (query.OnlyWithAttention)
        {
            joined = joined.Where(x =>
                x.o.Status == TransportOrderStatus.Submitted
                || x.o.WeightKg == null
                || x.o.Stops.Count == 0
                || x.o.Stops.Any(s => s.AppointmentRequired));
        }

        var totalCount = await joined.CountAsync(cancellationToken);
        var rows = await joined
            // Urgent work first (string enum: explicit rank), then oldest date.
            .OrderByDescending(x => x.o.Priority == OrderPriority.Urgent ? 3
                : x.o.Priority == OrderPriority.High ? 2
                : x.o.Priority == OrderPriority.Normal ? 1 : 0)
            .ThenBy(x => x.o.OrderDate).ThenBy(x => x.o.OrderNumber)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(x => new
            {
                x.o.Id, x.o.OrderNumber, x.o.OrderDate, x.o.CustomerId, x.CustomerName, x.o.Status,
                x.o.Priority, x.o.GoodsDescription, x.o.WeightKg, x.o.VolumeM3,
                x.o.AdrRequired, x.o.CraneRequired,
            })
            .ToListAsync(cancellationToken);

        var pageOrderIds = rows.Select(r => r.Id).ToList();
        var stops = pageOrderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && pageOrderIds.Contains(s.TransportOrderId))
                .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == tenantId),
                    s => s.LocationId, l => l.Id,
                    (s, locations) => new { Stop = s, Locations = locations })
                .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new
                {
                    x.Stop.TransportOrderId, x.Stop.Sequence, x.Stop.StopType,
                    x.Stop.RequestedFrom, x.Stop.RequestedTo, x.Stop.PlannedFrom, x.Stop.PlannedTo,
                    x.Stop.AppointmentRequired,
                    City = x.Stop.City ?? (l != null ? l.City : null),
                })
                .ToListAsync(cancellationToken);
        var stopsByOrder = stops.ToLookup(s => s.TransportOrderId);

        var cargoCounts = pageOrderIds.Count == 0
            ? []
            : await _dbContext.CargoItems.AsNoTracking()
                .Where(c => c.TenantId == tenantId && pageOrderIds.Contains(c.TransportOrderId))
                .GroupBy(c => c.TransportOrderId)
                .Select(g => new
                {
                    OrderId = g.Key,
                    Count = g.Count(),
                    Weight = g.Sum(c => c.TotalWeightKg ?? c.WeightPerUnitKg * c.ExpectedQuantity),
                    Volume = g.Sum(c => c.VolumeM3 * c.ExpectedQuantity),
                })
                .ToDictionaryAsync(g => g.OrderId, cancellationToken);
        var packageCounts = pageOrderIds.Count == 0
            ? []
            : await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == tenantId && pageOrderIds.Contains(p.TransportOrderId))
                .GroupBy(p => p.TransportOrderId)
                .Select(g => new { OrderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.OrderId, g => g.Count, cancellationToken);

        var items = rows.Select(r =>
        {
            var orderStops = stopsByOrder[r.Id].OrderBy(s => s.Sequence).ToList();
            var cargo = cargoCounts.GetValueOrDefault(r.Id);
            var weight = cargo?.Weight is > 0 ? cargo.Weight : r.WeightKg;
            var volume = cargo?.Volume is > 0 ? cargo.Volume : r.VolumeM3;

            var badges = new List<string>();
            if (r.Status == TransportOrderStatus.Submitted) badges.Add("SubmittedReview");
            if (orderStops.Count == 0) badges.Add("NoStops");
            if (weight is null) badges.Add("MissingWeight");
            if (volume is null) badges.Add("MissingVolume");
            if (orderStops.Any(s => s.AppointmentRequired)) badges.Add("AppointmentRequired");

            return new UnplannedOrderDto(
                r.Id, r.OrderNumber, r.OrderDate, r.CustomerId, r.CustomerName, r.Status, r.Priority,
                orderStops.FirstOrDefault(s => s.StopType == StopType.Loading)?.City,
                orderStops.LastOrDefault(s => s.StopType == StopType.Unloading)?.City,
                orderStops.Min(s => s.RequestedFrom ?? s.PlannedFrom),
                orderStops.Max(s => s.RequestedTo ?? s.PlannedTo),
                r.GoodsDescription,
                orderStops.Count,
                cargo?.Count ?? 0,
                packageCounts.GetValueOrDefault(r.Id),
                weight, volume,
                r.AdrRequired, r.CraneRequired,
                badges);
        }).ToList();

        return new PagedResult<UnplannedOrderDto>(items, totalCount, page.Page, page.PageSize);
    }

    // -----------------------------------------------------------------------
    // Resources
    // -----------------------------------------------------------------------

    public async Task<PlanningResourcesDto> GetResourcesAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var (start, end) = NormalizeRange(from, to);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // Trips in range once; grouped per resource for the availability strips.
        var rangeTrips = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.TripDate >= start && t.TripDate <= end
                        && t.Status != TripStatus.Cancelled)
            .Select(t => new { t.Id, t.TripNumber, t.TripDate, t.Status, t.DriverId, t.VehicleId, t.TrailerId })
            .ToListAsync(cancellationToken);
        var tripsByDriver = rangeTrips.Where(t => t.DriverId != null).ToLookup(t => t.DriverId!.Value);
        var tripsByVehicle = rangeTrips.Where(t => t.VehicleId != null).ToLookup(t => t.VehicleId!.Value);
        var tripsByTrailer = rangeTrips.Where(t => t.TrailerId != null).ToLookup(t => t.TrailerId!.Value);

        // Drivers + employee names.
        var driverRows = await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .Join(_dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId),
                d => d.EmployeeId, e => e.Id,
                (d, e) => new
                {
                    d.Id, d.DriverNumber, d.AvailabilityStatus, d.IsActive, d.IsBlocked, d.BlockReason,
                    d.EmployeeId, Name = e.FirstName + " " + e.LastName,
                })
            .OrderBy(d => d.Name)
            .Take(500)
            .ToListAsync(cancellationToken);
        var employeeIds = driverRows.Select(d => d.EmployeeId).ToList();

        var absences = employeeIds.Count == 0
            ? []
            : await _dbContext.Absences.AsNoTracking()
                .Where(a => a.TenantId == tenantId && employeeIds.Contains(a.EmployeeId)
                            && a.Status == AbsenceStatus.Approved
                            && a.StartDate <= end && a.EndDate >= start)
                .Select(a => new { a.EmployeeId, a.StartDate, a.EndDate, a.Type })
                .ToListAsync(cancellationToken);
        var absencesByEmployee = absences.ToLookup(a => a.EmployeeId);

        var warningDays = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (int?)s.QualificationExpiryWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultExpiryWarningDays;
        var qualifications = employeeIds.Count == 0
            ? []
            : await _dbContext.EmployeeQualifications.AsNoTracking()
                .Where(q => q.TenantId == tenantId && employeeIds.Contains(q.EmployeeId))
                .Join(_dbContext.QualificationTypes.AsNoTracking(), q => q.QualificationTypeId, t => t.Id,
                    (q, t) => new { Qualification = q, TypeName = t.Name })
                .ToListAsync(cancellationToken);
        var qualificationsByEmployee = qualifications.ToLookup(q => q.Qualification.EmployeeId);

        // Vehicle-side standing assignments (single source of truth).
        var vehicleRows = await _dbContext.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == tenantId)
            .OrderBy(v => v.InternalNumber)
            .Take(500)
            .Select(v => new
            {
                v.Id, v.InternalNumber, v.LicensePlate, v.OperationalStatus, v.IsActive,
                v.PayloadKg, v.VolumeM3, v.HasCrane, v.HasTailLift, v.HasRefrigeration, v.AdrSuitable,
                v.FixedDriverId, v.CurrentDriverId,
            })
            .ToListAsync(cancellationToken);
        var trailerRows = await _dbContext.Trailers.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.InternalNumber)
            .Take(500)
            .Select(t => new
            {
                t.Id, t.InternalNumber, t.LicensePlate, t.OperationalStatus, t.IsActive,
                t.CapacityKg, t.VolumeM3, t.HasRefrigeration, t.AdrSuitable,
            })
            .ToListAsync(cancellationToken);

        var driverNamesById = driverRows.ToDictionary(d => d.Id, d => d.Name);
        var fixedVehicleByDriver = vehicleRows
            .Where(v => v.FixedDriverId != null)
            .GroupBy(v => v.FixedDriverId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        // Overdue maintenance/inspection per asset (date-based; the fleet dashboard owns the
        // odometer-based variant).
        var overdueMaintenance = await _dbContext.MaintenanceRecords.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && (m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress)
                        && m.ScheduledDate != null && m.ScheduledDate < today)
            .Select(m => new { m.VehicleId, m.TrailerId })
            .ToListAsync(cancellationToken);
        var overdueInspections = await _dbContext.Inspections.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.CompletedDate == null && i.DueDate < today)
            .Select(i => new { i.VehicleId, i.TrailerId })
            .ToListAsync(cancellationToken);
        var maintenanceByVehicle = overdueMaintenance.Where(m => m.VehicleId != null).ToLookup(m => m.VehicleId!.Value);
        var maintenanceByTrailer = overdueMaintenance.Where(m => m.TrailerId != null).ToLookup(m => m.TrailerId!.Value);
        var inspectionsByVehicle = overdueInspections.Where(i => i.VehicleId != null).ToLookup(i => i.VehicleId!.Value);
        var inspectionsByTrailer = overdueInspections.Where(i => i.TrailerId != null).ToLookup(i => i.TrailerId!.Value);

        var drivers = driverRows.Select(d =>
        {
            var blocks = new List<string>();
            var warnings = new List<string>();
            foreach (var entry in qualificationsByEmployee[d.EmployeeId])
            {
                var status = _statusCalculator.CalculateEffectiveStatus(entry.Qualification, today, warningDays);
                switch (status)
                {
                    case QualificationStatus.Expired:
                    case QualificationStatus.Suspended:
                    case QualificationStatus.Rejected:
                        blocks.Add(entry.TypeName);
                        break;
                    case QualificationStatus.ExpiringSoon:
                        warnings.Add(entry.TypeName);
                        break;
                }
            }

            var fixedVehicle = fixedVehicleByDriver.GetValueOrDefault(d.Id);
            return new PlanningDriverDto(
                d.Id, d.DriverNumber, d.Name, d.AvailabilityStatus, d.IsActive, d.IsBlocked, d.BlockReason,
                fixedVehicle?.Id, fixedVehicle?.InternalNumber,
                tripsByDriver[d.Id].OrderBy(t => t.TripDate)
                    .Select(t => new ResourceAssignmentDto(t.TripDate, t.Id, t.TripNumber, t.Status)).ToList(),
                absencesByEmployee[d.EmployeeId]
                    .Select(a => new ResourceAbsenceDto(a.StartDate, a.EndDate, a.Type)).ToList(),
                blocks, warnings);
        }).ToList();

        var vehicles = vehicleRows.Select(v => new PlanningVehicleDto(
            v.Id, v.InternalNumber, v.LicensePlate, v.OperationalStatus, v.IsActive,
            v.PayloadKg, v.VolumeM3, v.HasCrane, v.HasTailLift, v.HasRefrigeration, v.AdrSuitable,
            v.FixedDriverId, v.FixedDriverId is { } fd ? driverNamesById.GetValueOrDefault(fd) : null,
            v.CurrentDriverId, v.CurrentDriverId is { } cd ? driverNamesById.GetValueOrDefault(cd) : null,
            maintenanceByVehicle[v.Id].Count(), inspectionsByVehicle[v.Id].Count(),
            tripsByVehicle[v.Id].OrderBy(t => t.TripDate)
                .Select(t => new ResourceAssignmentDto(t.TripDate, t.Id, t.TripNumber, t.Status)).ToList())).ToList();

        var trailers = trailerRows.Select(t => new PlanningTrailerDto(
            t.Id, t.InternalNumber, t.LicensePlate, t.OperationalStatus, t.IsActive,
            t.CapacityKg, t.VolumeM3, t.HasRefrigeration, t.AdrSuitable,
            maintenanceByTrailer[t.Id].Count(), inspectionsByTrailer[t.Id].Count(),
            tripsByTrailer[t.Id].OrderBy(x => x.TripDate)
                .Select(x => new ResourceAssignmentDto(x.TripDate, x.Id, x.TripNumber, x.Status)).ToList())).ToList();

        return new PlanningResourcesDto(start, end, drivers, vehicles, trailers);
    }
}
