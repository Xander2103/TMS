using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierActivityPlanningService
{
    /// <summary>
    /// Puts the activity's transport order on a trip. Returns null when the dossier does not exist
    /// in the tenant; throws <see cref="DossierVersionConflictException"/> on a stale token and
    /// <see cref="DomainValidationException"/> for every refusal (Dutch, 400).
    /// </summary>
    Task<DossierDetailDto?> PlanAsync(
        Guid dossierId, Guid activityId, PlanDossierActivityRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// D1 (2026-09-21): "Inplannen" from the dossier. Driver, vehicle and trailer live ONLY on
/// <see cref="Trip"/>, so planning an activity means: give the activity's ORDER a trip. This
/// service adds no second write path — the trip is created by the existing
/// <see cref="ITripService.CreateAsync"/> (number claim, reference checks, "only confirmed orders",
/// planning-entry sync, costing, fixed-vehicle proposal) and every later change of driver/vehicle/
/// trailer runs through the existing trip endpoints.
/// </summary>
public class DossierActivityPlanningService : IDossierActivityPlanningService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDossierService _dossierService;
    private readonly ITripService _tripService;

    public DossierActivityPlanningService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IDossierService dossierService,
        ITripService tripService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _dossierService = dossierService;
        _tripService = tripService;
    }

    public async Task<DossierDetailDto?> PlanAsync(
        Guid dossierId, Guid activityId, PlanDossierActivityRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await _dbContext.TransportDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Een gesloten dossier kan niet worden ingepland. Heropen het dossier eerst.");
        }

        if (request.Version is { } expected && expected != dossier.Version)
        {
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossierId, cancellationToken))!);
        }

        var activity = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DossierId == dossierId && a.Id == activityId)
            .Select(a => new { a.LinkedTransportOrderId, a.PlannedDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (activity is null)
        {
            throw new DomainValidationException("Deze activiteit bestaat niet.");
        }

        if (activity.LinkedTransportOrderId is not { } orderId)
        {
            throw new DomainValidationException(
                "Deze activiteit heeft geen transportopdracht en kan daarom niet op een rit worden gezet. "
                + "Alleen activiteiten met een opdracht worden ingepland.");
        }

        // Every incoming id — and the stored order link — is proven to be this tenant's.
        await _dbContext.TransportOrders.EnsureBelongsToTenantAsync(orderId, tenantId, "De gekoppelde opdracht", cancellationToken);
        await _dbContext.Drivers.EnsureBelongsToTenantAsync(request.DriverId, tenantId, "De gekozen chauffeur", cancellationToken);
        await _dbContext.Vehicles.EnsureBelongsToTenantAsync(request.VehicleId, tenantId, "Het gekozen voertuig", cancellationToken);
        await _dbContext.Trailers.EnsureBelongsToTenantAsync(request.TrailerId, tenantId, "De gekozen oplegger", cancellationToken);

        // Idempotent: an order that already sits on an open trip keeps that trip untouched — no
        // second trip, and no silent re-assignment (changing it is the trip page's job).
        var alreadyPlanned = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && to.TransportOrderId == orderId)
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == tenantId
                    && t.Status != TripStatus.Completed && t.Status != TripStatus.Cancelled),
                to => to.TripId, t => t.Id, (to, t) => t.Id)
            .AnyAsync(cancellationToken);
        if (alreadyPlanned)
        {
            return await _dossierService.GetAsync(dossierId, cancellationToken);
        }

        var tripDate = request.TripDate
            ?? activity.PlannedDate
            ?? await FirstStopDateAsync(orderId, cancellationToken)
            ?? await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.Id == orderId)
                .Select(o => o.OrderDate)
                .FirstAsync(cancellationToken);

        var result = await _tripService.CreateAsync(
            new CreateTripRequest(
                tripDate, request.DriverId, request.VehicleId, request.TrailerId,
                PlannedStart: null, PlannedEnd: null, Notes: null, OrderIds: [orderId],
                VehicleSelectionSource: request.VehicleSelectionSource),
            cancellationToken);
        if (result.Outcome != TripOperationOutcome.Success)
        {
            throw new DomainValidationException(result.Error ?? "De rit kon niet worden aangemaakt.");
        }

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    /// <summary>Tenant-local day of the order's earliest planned (else requested) stop time.</summary>
    private async Task<DateOnly?> FirstStopDateAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var times = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TransportOrderId == orderId)
            .Select(s => new { s.PlannedFrom, s.RequestedFrom })
            .ToListAsync(cancellationToken);
        var first = times
            .Select(t => t.PlannedFrom ?? t.RequestedFrom)
            .Where(t => t is not null)
            .Min();
        if (first is not { } instant)
        {
            return null;
        }

        var zone = await TenantTimeZone.ForTenantAsync(_dbContext, tenantId, cancellationToken);
        return TenantTimeZone.ToLocalDate(instant, zone);
    }
}
