using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

public interface IDriverAppService
{
    /// <summary>Null when the signed-in user has no driver profile.</summary>
    Task<MyDashboardDto?> GetMyDashboardAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DriverDocumentDto>> ListMyDocumentsAsync(CancellationToken cancellationToken);

    /// <summary>Stream + filename, or null when the document is outside the driver's scope.</summary>
    Task<(Stream Content, string FileName)?> OpenMyDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>
/// The driver-app read surface. Strictly self-scoped: everything resolves through the
/// signed-in user's driver profile, and documents are limited to papers of the vehicles and
/// trailers on the driver's active trips — never HR, financial or unrelated files.
/// </summary>
public class DriverAppService : IDriverAppService
{
    private static readonly TripStatus[] ActiveStatuses = [TripStatus.Planned, TripStatus.InProgress];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ITripExecutionService _executionService;
    private readonly IFileStorageService _fileStorage;
    private readonly TimeProvider _timeProvider;

    public DriverAppService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        ITripExecutionService executionService,
        IFileStorageService fileStorage,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _executionService = executionService;
        _fileStorage = fileStorage;
        _timeProvider = timeProvider;
    }

    private async Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.EmployeeId != null)
            .Join(_dbContext.Drivers.AsNoTracking().Where(d => d.TenantId == _tenantContext.TenantId),
                u => u.EmployeeId, d => d.EmployeeId, (u, d) => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MyDashboardDto?> GetMyDashboardAsync(CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // My trips, next 7 days — reuses the existing self-scoped projection.
        var trips = await _executionService.ListMyTripsAsync(today, today.AddDays(7), cancellationToken);
        var current = trips.FirstOrDefault(t => t.Status == TripStatus.InProgress)
            ?? trips.FirstOrDefault(t => t.TripDate == today && t.Status == TripStatus.Planned);
        var next = trips.FirstOrDefault(t => t.Id != current?.Id && t.Status == TripStatus.Planned);

        string? nextStopCity = null;
        string? nextStopLocation = null;
        DateTime? nextStopPlannedFrom = null;
        DateTime? nextStopEta = null;
        Eta.Entities.EtaSource? nextStopEtaSource = null;
        var openStops = 0;

        if (current is not null)
        {
            var execution = await _executionService.GetExecutionAsync(current.Id, restrictToOwnDriver: true, cancellationToken);
            if (execution.Execution is { } dto)
            {
                var open = dto.Stops.Where(s => !StopStatusMachine.IsTerminal(s.Status)).ToList();
                openStops = open.Count;
                var nextStop = open.FirstOrDefault();
                if (nextStop is not null)
                {
                    nextStopCity = nextStop.City;
                    nextStopLocation = nextStop.LocationName;
                    nextStopPlannedFrom = nextStop.ConfirmedFrom ?? nextStop.PlannedFrom ?? nextStop.RequestedFrom;
                    var eta = await _dbContext.StopEtas.AsNoTracking()
                        .Where(e => e.TenantId == tenantId && e.TripId == current.Id
                                    && e.TransportOrderStopId == nextStop.TransportOrderStopId)
                        .Select(e => new { e.CurrentEta, e.Source })
                        .FirstOrDefaultAsync(cancellationToken);
                    nextStopEta = eta?.CurrentEta;
                    nextStopEtaSource = eta?.Source;
                }
            }
        }

        var myTripIds = trips.Select(t => t.Id).ToList();
        var unresolvedExceptions = myTripIds.Count == 0
            ? 0
            : await _dbContext.ExecutionExceptions.AsNoTracking()
                .CountAsync(e => e.TenantId == tenantId && myTripIds.Contains(e.TripId)
                                 && (e.Status == ExecutionExceptionStatus.Open
                                     || e.Status == ExecutionExceptionStatus.Investigating), cancellationToken);
        var activeIncidents = await _dbContext.Incidents.AsNoTracking()
            .CountAsync(i => i.TenantId == tenantId && i.DriverId == driverId
                             && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress), cancellationToken);

        return new MyDashboardDto(
            current, next,
            nextStopCity, nextStopLocation, nextStopPlannedFrom, nextStopEta, nextStopEtaSource,
            openStops, unresolvedExceptions, activeIncidents,
            trips.Count(t => t.TripDate == today));
    }

    /// <summary>Vehicle/trailer ids on the driver's active trips (today and later).</summary>
    private async Task<(List<Guid> VehicleIds, List<Guid> TrailerIds)> MyActiveAssetsAsync(
        Guid driverId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var assets = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.DriverId == driverId
                        && t.TripDate >= today.AddDays(-1) && ActiveStatuses.Contains(t.Status))
            .Select(t => new { t.VehicleId, t.TrailerId })
            .ToListAsync(cancellationToken);
        return (
            assets.Where(a => a.VehicleId != null).Select(a => a.VehicleId!.Value).Distinct().ToList(),
            assets.Where(a => a.TrailerId != null).Select(a => a.TrailerId!.Value).Distinct().ToList());
    }

    public async Task<IReadOnlyList<DriverDocumentDto>> ListMyDocumentsAsync(CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return [];
        }

        var tenantId = _tenantContext.TenantId;
        var (vehicleIds, trailerIds) = await MyActiveAssetsAsync(driverId.Value, cancellationToken);
        if (vehicleIds.Count == 0 && trailerIds.Count == 0)
        {
            return [];
        }

        var vehicleNumbers = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == tenantId && vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.InternalNumber, cancellationToken);
        var trailerNumbers = trailerIds.Count == 0
            ? []
            : await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && trailerIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.InternalNumber, cancellationToken);

        var documents = await _dbContext.FleetDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                        && ((d.VehicleId != null && vehicleIds.Contains(d.VehicleId.Value))
                            || (d.TrailerId != null && trailerIds.Contains(d.TrailerId.Value))))
            .OrderBy(d => d.DocumentType)
            .ToListAsync(cancellationToken);

        return documents.Select(d => new DriverDocumentDto(
            d.Id,
            d.VehicleId is not null ? "Vehicle" : "Trailer",
            d.VehicleId is { } v ? vehicleNumbers.GetValueOrDefault(v, "?")
                : d.TrailerId is { } tr ? trailerNumbers.GetValueOrDefault(tr, "?") : "?",
            d.DocumentType, d.CustomTypeName, d.DocumentNumber, d.ExpiryDate,
            !string.IsNullOrWhiteSpace(d.DocumentPath))).ToList();
    }

    public async Task<(Stream Content, string FileName)?> OpenMyDocumentAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        var (vehicleIds, trailerIds) = await MyActiveAssetsAsync(driverId.Value, cancellationToken);
        var document = await _dbContext.FleetDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == _tenantContext.TenantId
                                      && ((d.VehicleId != null && vehicleIds.Contains(d.VehicleId.Value))
                                          || (d.TrailerId != null && trailerIds.Contains(d.TrailerId.Value))),
                cancellationToken);
        if (document is null || string.IsNullOrWhiteSpace(document.DocumentPath))
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(document.DocumentPath, cancellationToken);
        var fileName = $"{document.DocumentType}-{document.DocumentNumber ?? document.Id.ToString("N")[..8]}"
            + Path.GetExtension(document.DocumentPath);
        return (stream, fileName);
    }
}
