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

    private Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken) =>
        Planning.Services.DriverIdentity.ResolveAsync(_dbContext, _tenantContext.TenantId, _currentUserContext.CurrentUserId, cancellationToken);

    public async Task<IncidentDetailDto?> CreateMineAsync(
        CreateDriverIncidentRequest request, CancellationToken cancellationToken)
    {
        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (driverId is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;

        // Offline replay: the same key returns the driver's OWN stored incident (and dispatch is
        // not notified twice). A key that belongs to someone else's incident reads as
        // non-existent — the replay must never echo another report (Security H1 class).
        if (request.ClientRequestId is { } clientRequestId)
        {
            var replayed = await _dbContext.Incidents.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.ClientRequestId == clientRequestId)
                .Select(i => new { i.Id, i.DriverId })
                .FirstOrDefaultAsync(cancellationToken);
            if (replayed is not null)
            {
                return replayed.DriverId == driverId
                    ? await _incidentService.GetAsync(replayed.Id, cancellationToken)
                    : null;
            }
        }

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
            // Always keyed: the key also marks the incident as driver-reported (its trip/driver are locked).
            ClientRequestId: request.ClientRequestId ?? Guid.NewGuid()), cancellationToken);

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
