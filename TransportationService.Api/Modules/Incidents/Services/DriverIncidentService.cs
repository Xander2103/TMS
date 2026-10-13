using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Incidents.Services;

public interface IDriverIncidentService
{
    /// <summary>Null when the signed-in user has no driver profile.</summary>
    Task<IncidentDetailDto?> CreateMineAsync(CreateDriverIncidentRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentListItemDto>> ListMineAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Driver-side incident reporting on top of the EXISTING incident register (no mobile-only
/// table). Links are validated against the driver's own trips, the driver is stamped on the
/// incident, replay is idempotent via ClientRequestId, and dispatch is notified.
/// </summary>
public class DriverIncidentService : IDriverIncidentService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IIncidentService _incidentService;
    private readonly INotificationService _notificationService;

    public DriverIncidentService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IIncidentService incidentService,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _incidentService = incidentService;
        _notificationService = notificationService;
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

    public async Task<IncidentDetailDto?> CreateMineAsync(
        CreateDriverIncidentRequest request, CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;

        // Every link must point at the driver's own work; a foreign id reads as non-existent.
        if (request.TripId is { } tripId)
        {
            var trip = await _dbContext.Trips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == tenantId && t.DriverId == driverId,
                    cancellationToken);
            if (trip is null)
            {
                throw new DomainValidationException("tripId", "Deze rit is niet aan jou toegewezen.");
            }

            if (request.VehicleId is { } vehicleId && trip.VehicleId != vehicleId)
            {
                throw new DomainValidationException("vehicleId", "Dit voertuig hoort niet bij de gekoppelde rit.");
            }

            if (request.TrailerId is { } trailerId && trip.TrailerId != trailerId)
            {
                throw new DomainValidationException("trailerId", "Deze oplegger hoort niet bij de gekoppelde rit.");
            }

            if (request.TransportOrderId is { } orderId
                && !await _dbContext.TripOrders.AsNoTracking().AnyAsync(
                    o => o.TenantId == tenantId && o.TripId == tripId && o.TransportOrderId == orderId,
                    cancellationToken))
            {
                throw new DomainValidationException("transportOrderId", "Deze opdracht staat niet op de gekoppelde rit.");
            }
        }
        else if (request.VehicleId is not null || request.TrailerId is not null || request.TransportOrderId is not null)
        {
            throw new DomainValidationException("tripId", "Koppel eerst de rit waarop het probleem zich voordeed.");
        }

        var incident = await _incidentService.CreateAsync(new SaveIncidentRequest(
            request.Title, request.Description, request.IncidentType, request.Severity,
            CustomTypeName: request.CustomTypeName,
            DriverId: driverId,
            VehicleId: request.VehicleId,
            TrailerId: request.TrailerId,
            TransportOrderId: request.TransportOrderId,
            TripId: request.TripId,
            ClientRequestId: request.ClientRequestId), cancellationToken);

        // Dispatch hears about driver-reported incidents immediately.
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.OperationsView, "incident_reported",
            "Incident gemeld door chauffeur",
            $"\"{incident.Title}\" ({incident.Severity}) is gemeld vanuit de chauffeursapp.",
            $"/incidents/{incident.Id}", cancellationToken);

        return incident;
    }

    public async Task<IReadOnlyList<IncidentListItemDto>> ListMineAsync(CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return [];
        }

        return await _incidentService.ListForDriverAsync(driverId.Value, cancellationToken);
    }
}
