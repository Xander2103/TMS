using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Warehousing.Services;

public sealed record WarehouseTraceEventDto(
    string EventType, DateTime OccurredAt, string? LocationCode, string? TripNumber, string? Result);

public sealed record WarehouseTraceDto(
    Guid PackageId, string PackageNumber, string? Description, string LifecycleStatus, string ExceptionStatus,
    string OrderNumber, string CustomerName,
    Guid? WarehouseLocationId, string? WarehouseName, string? LocationCode, string? LocationName,
    IReadOnlyList<WarehouseTraceEventDto> LastEvents);

public sealed record WarehouseOverviewPackageDto(
    Guid PackageId, string PackageNumber, string OrderNumber, string LifecycleStatus, string? LocationCode);

public sealed record WarehouseOverviewLocationDto(
    Guid LocationId, string Code, string Name, string Kind, IReadOnlyList<WarehouseOverviewPackageDto> Packages);

public sealed record WarehouseOverviewDto(
    Guid WarehouseId, string WarehouseName,
    IReadOnlyList<WarehouseOverviewLocationDto> Locations,
    /// <summary>Still here while their order sits on a trip dated TODAY — "should have left".</summary>
    IReadOnlyList<WarehouseOverviewPackageDto> ShouldHaveLeftToday,
    /// <summary>Still here while their order sits on a trip dated TOMORROW — "waits for tomorrow".</summary>
    IReadOnlyList<WarehouseOverviewPackageDto> WaitsForTomorrow);

public interface IWarehouseTraceService
{
    Task<WarehouseTraceDto?> TraceAsync(string barcode, CancellationToken cancellationToken);
    Task<WarehouseOverviewDto?> GetOverviewAsync(Guid warehouseId, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// Wave 4 §4: the warehouse trace answers — "where is X", "what should have left today",
/// "what waits for tomorrow" — read straight from the location projection and the append-only
/// custody chain; nothing here mutates state.
/// </summary>
public class WarehouseTraceService : IWarehouseTraceService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPackageBarcodeService _barcodes;

    public WarehouseTraceService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IPackageBarcodeService barcodes)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _barcodes = barcodes;
    }

    public async Task<WarehouseTraceDto?> TraceAsync(string barcode, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var resolution = await _barcodes.ResolveAsync(barcode.Trim(), cancellationToken);
        if (resolution is null)
        {
            return null;
        }

        var package = resolution.Package;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Id == package.TransportOrderId)
            .Join(_dbContext.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new { o.OrderNumber, CustomerName = c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var location = package.CurrentWarehouseLocationId is { } locationId
            ? await _dbContext.WarehouseLocations.AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.Id == locationId)
                .Join(_dbContext.Warehouses.AsNoTracking(), l => l.WarehouseId, w => w.Id,
                    (l, w) => new { l.Code, l.Name, WarehouseName = w.Name })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var events = await _dbContext.PackageEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.PackageId == package.Id)
            .OrderByDescending(e => e.OccurredAt)
            .Take(10)
            .Select(e => new { e.EventType, e.OccurredAt, e.WarehouseLocationId, e.TripId, e.Result })
            .ToListAsync(cancellationToken);
        var eventLocationIds = events.Where(e => e.WarehouseLocationId != null)
            .Select(e => e.WarehouseLocationId!.Value).Distinct().ToList();
        var locationCodes = eventLocationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.WarehouseLocations.AsNoTracking()
                .Where(l => l.TenantId == tenantId && eventLocationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);
        var tripIds = events.Where(e => e.TripId != null).Select(e => e.TripId!.Value).Distinct().ToList();
        var tripNumbers = tripIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenantId && tripIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.TripNumber, cancellationToken);

        return new WarehouseTraceDto(
            package.Id, package.PackageNumber, package.Description,
            package.CurrentLifecycleStatus.ToString(), package.CurrentExceptionStatus.ToString(),
            order?.OrderNumber ?? "?", order?.CustomerName ?? "?",
            package.CurrentWarehouseLocationId, location?.WarehouseName, location?.Code, location?.Name,
            events.Select(e => new WarehouseTraceEventDto(
                e.EventType.ToString(), e.OccurredAt,
                e.WarehouseLocationId is { } lid ? locationCodes.GetValueOrDefault(lid) : null,
                e.TripId is { } tid ? tripNumbers.GetValueOrDefault(tid) : null,
                e.Result)).ToList());
    }

    public async Task<WarehouseOverviewDto?> GetOverviewAsync(
        Guid warehouseId, DateOnly today, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var warehouseName = await _dbContext.Warehouses.AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.Id == warehouseId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (warehouseName is null)
        {
            return null;
        }

        var locations = await _dbContext.WarehouseLocations.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.WarehouseId == warehouseId)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code)
            .ToListAsync(cancellationToken);
        var locationIds = locations.Select(l => l.Id).ToList();
        var codeById = locations.ToDictionary(l => l.Id, l => l.Code);

        var packagesHere = locationIds.Count == 0
            ? []
            : await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.CurrentWarehouseLocationId != null
                            && locationIds.Contains(p.CurrentWarehouseLocationId.Value))
                .Join(_dbContext.TransportOrders.AsNoTracking(), p => p.TransportOrderId, o => o.Id,
                    (p, o) => new
                    {
                        p.Id, p.PackageNumber, p.CurrentLifecycleStatus, p.CurrentWarehouseLocationId,
                        p.TransportOrderId, o.OrderNumber,
                    })
                .ToListAsync(cancellationToken);

        var packageDtos = packagesHere
            .Select(p => new
            {
                p.TransportOrderId,
                LocationId = p.CurrentWarehouseLocationId!.Value,
                Dto = new WarehouseOverviewPackageDto(
                    p.Id, p.PackageNumber, p.OrderNumber, p.CurrentLifecycleStatus.ToString(),
                    codeById.GetValueOrDefault(p.CurrentWarehouseLocationId!.Value)),
            })
            .ToList();

        var byLocation = locations
            .Select(l => new WarehouseOverviewLocationDto(
                l.Id, l.Code, l.Name, l.Kind,
                packageDtos.Where(p => p.LocationId == l.Id)
                    .OrderBy(p => p.Dto.PackageNumber)
                    .Select(p => p.Dto)
                    .ToList()))
            .ToList();

        // Orders of the packages that sit here, joined to their planned/active trips.
        var hereOrderIds = packagesHere.Select(p => p.TransportOrderId).Distinct().ToList();
        var tripDates = hereOrderIds.Count == 0
            ? []
            : await _dbContext.TripOrders.AsNoTracking()
                .Where(to => to.TenantId == tenantId && hereOrderIds.Contains(to.TransportOrderId))
                .Join(_dbContext.Trips.AsNoTracking()
                        .Where(t => t.Status == Modules.Planning.Entities.TripStatus.Planned
                                    || t.Status == Modules.Planning.Entities.TripStatus.InProgress),
                    to => to.TripId, t => t.Id,
                    (to, t) => new { to.TransportOrderId, t.TripDate })
                .ToListAsync(cancellationToken);
        var todayOrders = tripDates.Where(t => t.TripDate == today).Select(t => t.TransportOrderId).ToHashSet();
        var tomorrowOrders = tripDates.Where(t => t.TripDate == today.AddDays(1)).Select(t => t.TransportOrderId).ToHashSet();

        return new WarehouseOverviewDto(
            warehouseId, warehouseName, byLocation,
            packageDtos.Where(p => todayOrders.Contains(p.TransportOrderId)).Select(p => p.Dto).ToList(),
            packageDtos.Where(p => tomorrowOrders.Contains(p.TransportOrderId)).Select(p => p.Dto).ToList());
    }
}
