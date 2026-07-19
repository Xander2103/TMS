using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

/// <summary>
/// Warehouse loading view for one day: which trips must be loaded, how complete they are.
/// DELIBERATELY excludes HR, cost and profitability data — the DTO is the contract.
/// </summary>
public record WarehouseTripDto(
    Guid TripId,
    string TripNumber,
    DateOnly TripDate,
    string Status,
    string? DriverName,
    string? VehicleNumber,
    int OrderCount,
    int TotalPackages,
    int MandatoryPackages,
    int LoadedCount,
    int NotLoadedCount,
    int MissingCount,
    int DamagedCount,
    int OpenExceptionCount,
    bool IsComplete,
    PackageDepartureRule Rule,
    IReadOnlyList<WarehouseTripStopDto> LoadingStops);

public record WarehouseTripStopDto(Guid StopId, string? LocationName, string? City, int ExpectedPackages);

public record WarehousePackageSearchRowDto(
    Guid PackageId,
    string PackageNumber,
    string Description,
    PackageLifecycleStatus Status,
    PackageExceptionState ExceptionState,
    Guid TransportOrderId,
    string OrderNumber,
    Guid? TripId,
    string? TripNumber);

public interface IWarehouseService
{
    Task<IReadOnlyList<WarehouseTripDto>> ListTripsAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehousePackageSearchRowDto>> SearchPackagesAsync(string search, CancellationToken cancellationToken);
}

public class WarehouseService : IWarehouseService
{
    private const int SearchCap = 50;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ITripPackageService _tripPackageService;

    public WarehouseService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ITripPackageService tripPackageService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _tripPackageService = tripPackageService;
    }

    public async Task<IReadOnlyList<WarehouseTripDto>> ListTripsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var trips = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .Where(t => t.TenantId == _tenantContext.TenantId && t.TripDate == date
                        && (t.Status == TripStatus.Planned || t.Status == TripStatus.InProgress))
            .OrderBy(t => t.TripNumber)
            .ToListAsync(cancellationToken);

        var driverIds = trips.Where(t => t.DriverId is not null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var driverNames = driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking().Where(e => e.TenantId == _tenantContext.TenantId),
                    d => d.EmployeeId, e => e.Id, (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var vehicleIds = trips.Where(t => t.VehicleId is not null).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var vehicleNumbers = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == _tenantContext.TenantId && vehicleIds.Contains(v.Id))
                .Select(v => new { v.Id, Number = v.InternalNumber + " (" + v.LicensePlate + ")" })
                .ToDictionaryAsync(x => x.Id, x => x.Number, cancellationToken);

        var result = new List<WarehouseTripDto>(trips.Count);
        foreach (var trip in trips)
        {
            var readiness = await _tripPackageService.EvaluateReadinessAsync(trip, cancellationToken);
            var checklistResult = await _tripPackageService.GetChecklistAsync(
                trip.Id, stopId: null, restrictToOwnDriver: false, cancellationToken);
            var loadingStops = (checklistResult.Payload as TripPackageChecklistDto)?.Stops
                .Where(s => s.StopType == StopType.Loading.ToString())
                .Select(s => new WarehouseTripStopDto(s.StopId, s.LocationName, s.City, s.Packages.Count))
                .ToList() ?? [];

            result.Add(new WarehouseTripDto(
                trip.Id, trip.TripNumber, trip.TripDate, trip.Status.ToString(),
                trip.DriverId is { } d ? driverNames.GetValueOrDefault(d) : null,
                trip.VehicleId is { } v ? vehicleNumbers.GetValueOrDefault(v) : null,
                trip.Orders.Count(o => !o.IsDeleted),
                readiness.TotalPackages, readiness.MandatoryPackages, readiness.LoadedCount,
                readiness.NotLoadedCount, readiness.MissingCount, readiness.DamagedCount,
                readiness.OpenExceptionCount, readiness.IsComplete, readiness.Rule,
                loadingStops));
        }

        return result;
    }

    public async Task<IReadOnlyList<WarehousePackageSearchRowDto>> SearchPackagesAsync(
        string search, CancellationToken cancellationToken)
    {
        var term = search.Trim().ToLowerInvariant();
        if (term.Length < 2)
        {
            return [];
        }

        var rows = await _dbContext.Packages.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .Where(p => p.PackageNumber.ToLower().Contains(term)
                        || p.BarcodeValue.ToLower().Contains(term)
                        || (p.ExternalBarcode != null && p.ExternalBarcode.ToLower().Contains(term))
                        || p.Description.ToLower().Contains(term))
            .OrderByDescending(p => p.CreatedAt)
            .Take(SearchCap)
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == _tenantContext.TenantId),
                p => p.TransportOrderId, o => o.Id,
                (p, o) => new { Package = p, o.OrderNumber })
            .ToListAsync(cancellationToken);

        var orderIds = rows.Select(r => r.Package.TransportOrderId).Distinct().ToList();
        var tripByOrder = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == _tenantContext.TenantId && orderIds.Contains(to.TransportOrderId))
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == _tenantContext.TenantId),
                to => to.TripId, t => t.Id,
                (to, t) => new { to.TransportOrderId, t.Id, t.TripNumber, t.CreatedAt })
            .GroupBy(x => x.TransportOrderId)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToListAsync(cancellationToken);
        var tripLookup = tripByOrder.ToDictionary(x => x.TransportOrderId, x => (x.Id, x.TripNumber));

        return rows.Select(r => new WarehousePackageSearchRowDto(
                r.Package.Id, r.Package.PackageNumber, r.Package.Description,
                r.Package.CurrentLifecycleStatus, r.Package.CurrentExceptionStatus,
                r.Package.TransportOrderId, r.OrderNumber,
                tripLookup.TryGetValue(r.Package.TransportOrderId, out var trip) ? trip.Id : null,
                tripLookup.TryGetValue(r.Package.TransportOrderId, out var trip2) ? trip2.TripNumber : null))
            .ToList();
    }
}
