using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

/// <summary>
/// FL-B1/M8: counts every operational or historical record that points at a vehicle or trailer.
/// The same numbers drive the delete guard (refuse when anything references the asset), the
/// deactivation warning (upcoming trips) and the UI explanation. Trailers use the same shape
/// (no tank cards, no driver slots).
/// </summary>
public static class FleetReferences
{
    private const int UpcomingTripLimit = 10;

    public static async Task<VehicleReferencesDto> ForVehicleAsync(
        TransportationDbContext db, Guid tenantId, Guid vehicleId, CancellationToken cancellationToken)
    {
        var trips = db.Trips.AsNoTracking().Where(t => t.TenantId == tenantId && t.VehicleId == vehicleId);
        var vehicle = await db.Vehicles.AsNoTracking().IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.Id == vehicleId)
            .Select(v => new { v.FixedDriverId, v.CurrentDriverId })
            .FirstOrDefaultAsync(cancellationToken);

        return new VehicleReferencesDto(
            PlannedTrips: await trips.CountAsync(t => t.Status == TripStatus.Planned || t.Status == TripStatus.InProgress, cancellationToken),
            CompletedTrips: await trips.CountAsync(t => t.Status == TripStatus.Completed, cancellationToken),
            OtherTrips: await trips.CountAsync(t => t.Status == TripStatus.Draft || t.Status == TripStatus.Cancelled, cancellationToken),
            Maintenance: await db.MaintenanceRecords.CountAsync(m => m.TenantId == tenantId && m.VehicleId == vehicleId, cancellationToken),
            Damage: await db.DamageReports.CountAsync(d => d.TenantId == tenantId && d.VehicleId == vehicleId, cancellationToken),
            Documents: await db.FleetDocuments.CountAsync(d => d.TenantId == tenantId && d.VehicleId == vehicleId, cancellationToken),
            Inspections: await db.Inspections.CountAsync(i => i.TenantId == tenantId && i.VehicleId == vehicleId, cancellationToken),
            Fuel: await db.FuelTransactions.CountAsync(f => f.TenantId == tenantId && f.VehicleId == vehicleId, cancellationToken),
            Leasing: await db.LeasingContracts.CountAsync(l => l.TenantId == tenantId && l.VehicleId == vehicleId, cancellationToken),
            Tachograph: await db.TachographCalibrations.CountAsync(c => c.TenantId == tenantId && c.VehicleId == vehicleId, cancellationToken),
            TankCards: await db.TankCards.CountAsync(c => c.TenantId == tenantId && c.VehicleId == vehicleId, cancellationToken),
            IsFixedDriverVehicle: vehicle?.FixedDriverId is not null,
            IsCurrentDriverVehicle: vehicle?.CurrentDriverId is not null,
            UpcomingTrips: await UpcomingAsync(trips, cancellationToken));
    }

    public static async Task<VehicleReferencesDto> ForTrailerAsync(
        TransportationDbContext db, Guid tenantId, Guid trailerId, CancellationToken cancellationToken)
    {
        var trips = db.Trips.AsNoTracking().Where(t => t.TenantId == tenantId && t.TrailerId == trailerId);

        return new VehicleReferencesDto(
            PlannedTrips: await trips.CountAsync(t => t.Status == TripStatus.Planned || t.Status == TripStatus.InProgress, cancellationToken),
            CompletedTrips: await trips.CountAsync(t => t.Status == TripStatus.Completed, cancellationToken),
            OtherTrips: await trips.CountAsync(t => t.Status == TripStatus.Draft || t.Status == TripStatus.Cancelled, cancellationToken),
            Maintenance: await db.MaintenanceRecords.CountAsync(m => m.TenantId == tenantId && m.TrailerId == trailerId, cancellationToken),
            Damage: await db.DamageReports.CountAsync(d => d.TenantId == tenantId && d.TrailerId == trailerId, cancellationToken),
            Documents: await db.FleetDocuments.CountAsync(d => d.TenantId == tenantId && d.TrailerId == trailerId, cancellationToken),
            Inspections: await db.Inspections.CountAsync(i => i.TenantId == tenantId && i.TrailerId == trailerId, cancellationToken),
            Fuel: 0,
            Leasing: await db.LeasingContracts.CountAsync(l => l.TenantId == tenantId && l.TrailerId == trailerId, cancellationToken),
            Tachograph: 0,
            TankCards: 0,
            IsFixedDriverVehicle: false,
            IsCurrentDriverVehicle: false,
            UpcomingTrips: await UpcomingAsync(trips, cancellationToken));
    }

    private static async Task<IReadOnlyList<TripReferenceDto>> UpcomingAsync(IQueryable<Trip> trips, CancellationToken cancellationToken) =>
        (await trips
            .Where(t => t.Status == TripStatus.Planned || t.Status == TripStatus.InProgress)
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .Take(UpcomingTripLimit)
            .Select(t => new { t.Id, t.TripNumber, t.TripDate, t.Status })
            .ToListAsync(cancellationToken))
        .Select(t => new TripReferenceDto(t.Id, t.TripNumber, t.TripDate, t.Status.ToString()))
        .ToList();

    /// <summary>
    /// The Dutch refusal sentence: what still points at the asset and what to do instead.
    /// <paramref name="noun"/> is "Dit voertuig" / "Deze oplegger".
    /// </summary>
    public static string RefusalMessage(string noun, string pronoun, VehicleReferencesDto refs)
    {
        var tripParts = new List<string>();
        if (refs.PlannedTrips > 0) tripParts.Add(Count(refs.PlannedTrips, "geplande rit", "geplande ritten"));
        if (refs.CompletedTrips > 0) tripParts.Add(Count(refs.CompletedTrips, "afgeronde rit", "afgeronde ritten"));
        if (refs.OtherTrips > 0) tripParts.Add(Count(refs.OtherTrips, "concept- of geannuleerde rit", "concept- of geannuleerde ritten"));

        var fleetParts = new List<string>();
        if (refs.Maintenance > 0) fleetParts.Add(Count(refs.Maintenance, "onderhoudsbeurt", "onderhoudsbeurten"));
        if (refs.Damage > 0) fleetParts.Add(Count(refs.Damage, "schademelding", "schademeldingen"));
        if (refs.Documents > 0) fleetParts.Add(Count(refs.Documents, "document", "documenten"));
        if (refs.Inspections > 0) fleetParts.Add(Count(refs.Inspections, "keuring", "keuringen"));
        if (refs.Fuel > 0) fleetParts.Add(Count(refs.Fuel, "tankbeurt", "tankbeurten"));
        if (refs.Leasing > 0) fleetParts.Add(Count(refs.Leasing, "leasingcontract", "leasingcontracten"));
        if (refs.Tachograph > 0) fleetParts.Add(Count(refs.Tachograph, "tachograafijking", "tachograafijkingen"));
        if (refs.TankCards > 0) fleetParts.Add(Count(refs.TankCards, "tankkaart", "tankkaarten"));

        var linked = new List<string>();
        if (tripParts.Count > 0) linked.Add(string.Join(", ", tripParts));
        if (fleetParts.Count > 0) linked.Add($"historische wagenparkgegevens ({string.Join(", ", fleetParts)})");

        return $"{noun} kan niet worden verwijderd omdat {pronoun} nog gekoppeld is aan {string.Join(" en ", linked)}. " +
               $"Deactiveer {pronoun} in plaats daarvan: de historiek blijft bewaard en {pronoun} kan niet meer ingepland worden.";
    }

    private static string Count(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";
}

public enum FleetDeleteOutcome
{
    Deleted,
    NotFound,
    Refused,
}

/// <summary>FL-B1: delete result; <c>Refused</c> carries the business message and the references.</summary>
public record FleetDeleteResult(FleetDeleteOutcome Outcome, string? Message = null, VehicleReferencesDto? References = null)
{
    public static readonly FleetDeleteResult Deleted = new(FleetDeleteOutcome.Deleted);
    public static readonly FleetDeleteResult NotFound = new(FleetDeleteOutcome.NotFound);
}
