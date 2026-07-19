using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

/// <summary>
/// Composes the fleet dashboard from the per-module overview queries so the SPA needs a single call.
/// Windows: maintenance/inspections due within 30 days, documents expiring within 60; lists show top 5.
/// </summary>
public class FleetDashboardService : IFleetDashboardService
{
    public const int MaintenanceWindowDays = 30;
    public const int InspectionWindowDays = 30;
    public const int DocumentWindowDays = 60;
    public const int TopN = 5;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IMaintenanceService _maintenanceService;
    private readonly IInspectionService _inspectionService;
    private readonly IFleetDocumentService _documentService;
    private readonly IDamageReportService _damageReportService;
    private readonly IFuelService _fuelService;

    public FleetDashboardService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IMaintenanceService maintenanceService,
        IInspectionService inspectionService,
        IFleetDocumentService documentService,
        IDamageReportService damageReportService,
        IFuelService fuelService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _maintenanceService = maintenanceService;
        _inspectionService = inspectionService;
        _documentService = documentService;
        _damageReportService = damageReportService;
        _fuelService = fuelService;
    }

    public async Task<FleetDashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var vehicles = await VehicleCountsAsync(cancellationToken);
        var trailers = await TrailerCountsAsync(cancellationToken);

        var maintenanceDue = await _maintenanceService.ListDueAsync(MaintenanceWindowDays, cancellationToken);
        var inspectionsDue = await _inspectionService.ListDueAsync(InspectionWindowDays, cancellationToken);
        var documentsExpiring = await _documentService.ListExpiringAsync(DocumentWindowDays, cancellationToken);
        var recentDamage = await _damageReportService.ListRecentAsync(TopN, cancellationToken);
        var fuelWarnings = await _fuelService.ListRecentWarningsAsync(TopN, cancellationToken);

        var openDamageCount = await _dbContext.DamageReports
            .Where(d => d.TenantId == _tenantContext.TenantId
                        && d.Status != DamageStatus.Repaired
                        && d.Status != DamageStatus.Closed)
            .CountAsync(cancellationToken);

        return new FleetDashboardDto(
            vehicles,
            trailers,
            maintenanceDue.Count,
            maintenanceDue.Take(TopN).ToList(),
            inspectionsDue.Count,
            inspectionsDue.Take(TopN).ToList(),
            documentsExpiring.Count,
            documentsExpiring.Take(TopN).ToList(),
            openDamageCount,
            recentDamage,
            fuelWarnings);
    }

    private async Task<FleetAssetCountsDto> VehicleCountsAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Vehicles
            .Where(v => v.TenantId == _tenantContext.TenantId)
            .GroupBy(v => new { v.OperationalStatus, v.IsActive })
            .Select(g => new { g.Key.OperationalStatus, g.Key.IsActive, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new FleetAssetCountsDto(
            rows.Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == VehicleOperationalStatus.Available).Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == VehicleOperationalStatus.InUse).Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == VehicleOperationalStatus.InMaintenance).Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == VehicleOperationalStatus.OutOfService).Sum(r => r.Count),
            rows.Where(r => !r.IsActive).Sum(r => r.Count));
    }

    private async Task<FleetAssetCountsDto> TrailerCountsAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Trailers
            .Where(t => t.TenantId == _tenantContext.TenantId)
            .GroupBy(t => new { t.OperationalStatus, t.IsActive })
            .Select(g => new { g.Key.OperationalStatus, g.Key.IsActive, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new FleetAssetCountsDto(
            rows.Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == TrailerOperationalStatus.Available).Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == TrailerOperationalStatus.InUse).Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == TrailerOperationalStatus.InMaintenance).Sum(r => r.Count),
            rows.Where(r => r.OperationalStatus == TrailerOperationalStatus.OutOfService).Sum(r => r.Count),
            rows.Where(r => !r.IsActive).Sum(r => r.Count));
    }
}
