using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

/// <summary>Whether a KPI value is measured, derived/estimated, or not computable from current data.</summary>
public enum KpiQuality { Actual, Estimated, Unavailable }

public record FleetKpiValueDto(string Key, string Label, decimal? Value, string Unit, KpiQuality Quality, string? Detail = null);

public record FleetKpiDto(Guid AssetId, DateOnly From, DateOnly To, IReadOnlyList<FleetKpiValueDto> Values);

public interface IFleetKpiService
{
    Task<FleetKpiDto?> GetVehicleKpiAsync(Guid vehicleId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<FleetKpiDto?> GetTrailerKpiAsync(Guid trailerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// Per-vehicle / per-trailer KPI read model built strictly from existing data (fuel,
/// maintenance, damage, trips). Every value carries a quality flag; nothing is invented —
/// a KPI with no source data is reported as Unavailable rather than a fake 0.
/// </summary>
public class FleetKpiService : IFleetKpiService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public FleetKpiService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<FleetKpiDto?> GetVehicleKpiAsync(Guid vehicleId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (!await _dbContext.Vehicles.AnyAsync(v => v.TenantId == tenantId && v.Id == vehicleId, cancellationToken))
        {
            return null;
        }

        var values = new List<FleetKpiValueDto>();

        var trips = await _dbContext.Trips.AsNoTracking()
            .CountAsync(t => t.TenantId == tenantId && t.VehicleId == vehicleId && t.TripDate >= from && t.TripDate <= to, cancellationToken);
        values.Add(new("trips", "Ritten", trips, "ritten", KpiQuality.Actual));

        var fuel = await _dbContext.FuelTransactions.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.VehicleId == vehicleId && f.TransactionDate >= from && f.TransactionDate <= to)
            .ToListAsync(cancellationToken);
        if (fuel.Count > 0)
        {
            var litres = fuel.Sum(f => f.Litres);
            var cost = fuel.Sum(f => f.TotalAmount);
            values.Add(new("fuel_litres", "Brandstof", Math.Round(litres, 1), "l", KpiQuality.Actual));
            values.Add(new("fuel_cost", "Brandstofkosten", Math.Round(cost, 2), "EUR", KpiQuality.Actual));

            // Km derived from the odometer span between the fills in the window.
            var odos = fuel.Where(f => f.OdometerKm is > 0).Select(f => f.OdometerKm!.Value).OrderBy(o => o).ToList();
            if (odos.Count >= 2)
            {
                var km = odos[^1] - odos[0];
                values.Add(new("km", "Kilometers", km, "km", KpiQuality.Estimated, "Afgeleid uit tankbeurten"));
                if (km > 0)
                {
                    values.Add(new("consumption", "Verbruik", Math.Round(litres / km * 100, 2), "l/100km", KpiQuality.Estimated));
                    values.Add(new("cost_per_km", "Kost per km", Math.Round(cost / km, 3), "EUR/km", KpiQuality.Estimated));
                }
            }
            else
            {
                values.Add(new("km", "Kilometers", null, "km", KpiQuality.Unavailable, "Onvoldoende odometergegevens"));
            }
        }
        else
        {
            values.Add(new("fuel_litres", "Brandstof", null, "l", KpiQuality.Unavailable));
            values.Add(new("km", "Kilometers", null, "km", KpiQuality.Unavailable));
        }

        values.AddRange(await MaintenanceAndDamageAsync(tenantId, vehicleId, trailer: false, from, to, cancellationToken));
        return new FleetKpiDto(vehicleId, from, to, values);
    }

    public async Task<FleetKpiDto?> GetTrailerKpiAsync(Guid trailerId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (!await _dbContext.Trailers.AnyAsync(t => t.TenantId == tenantId && t.Id == trailerId, cancellationToken))
        {
            return null;
        }

        var values = new List<FleetKpiValueDto>();
        var trips = await _dbContext.Trips.AsNoTracking()
            .CountAsync(t => t.TenantId == tenantId && t.TrailerId == trailerId && t.TripDate >= from && t.TripDate <= to, cancellationToken);
        values.Add(new("trips", "Ritten", trips, "ritten", KpiQuality.Actual));

        // Assigned days = distinct trip dates in the window (no fuel KPIs for trailers).
        var assignedDays = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.TrailerId == trailerId && t.TripDate >= from && t.TripDate <= to)
            .Select(t => t.TripDate).Distinct().CountAsync(cancellationToken);
        values.Add(new("assigned_days", "Ingezette dagen", assignedDays, "dagen", KpiQuality.Actual));

        values.AddRange(await MaintenanceAndDamageAsync(tenantId, trailerId, trailer: true, from, to, cancellationToken));
        return new FleetKpiDto(trailerId, from, to, values);
    }

    private async Task<List<FleetKpiValueDto>> MaintenanceAndDamageAsync(
        Guid tenantId, Guid assetId, bool trailer, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var values = new List<FleetKpiValueDto>();

        var maintenanceCost = await _dbContext.MaintenanceRecords.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && (trailer ? m.TrailerId == assetId : m.VehicleId == assetId)
                        && m.Status == MaintenanceStatus.Completed
                        && m.CompletedDate >= from && m.CompletedDate <= to)
            .SumAsync(m => (decimal?)m.Cost, cancellationToken);
        values.Add(new("maintenance_cost", "Onderhoudskosten", maintenanceCost is { } mc ? Math.Round(mc, 2) : 0m, "EUR", KpiQuality.Actual));

        var damage = await _dbContext.DamageReports.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                        && (trailer ? d.TrailerId == assetId : d.VehicleId == assetId)
                        && d.IncidentDate >= from && d.IncidentDate <= to)
            .ToListAsync(cancellationToken);
        values.Add(new("damage_cost", "Schadekosten", Math.Round(damage.Sum(d => d.RepairCost ?? 0m), 2), "EUR", KpiQuality.Actual));
        values.Add(new("downtime", "Stilstand", damage.Sum(d => d.DowntimeDays ?? 0), "dagen", KpiQuality.Actual));
        values.Add(new("incidents", "Incidenten", damage.Count, "incidenten", KpiQuality.Actual));

        return values;
    }
}
