using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IMaintenancePolicyService
{
    Task<IReadOnlyList<MaintenancePolicyDto>> ListAsync(CancellationToken cancellationToken);

    Task<MaintenancePolicyDto> CreateAsync(SaveMaintenancePolicyRequest request, CancellationToken cancellationToken);

    Task<MaintenancePolicyDto?> UpdateAsync(Guid id, SaveMaintenancePolicyRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Most-specific applicable rule: asset override → category rule → company default.</summary>
    Task<ResolvedMaintenancePolicy?> ResolveAsync(
        MaintenancePolicyKind kind, FleetAssetKind assetKind, Guid assetId, Guid? categoryId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Plans the initial maintenance job and inspection for a newly registered asset from the
    /// applicable policies. Idempotent: assets with an open planned job/inspection are skipped,
    /// and nothing is created when a policy lacks the data to compute a due date.
    /// </summary>
    Task ApplyDefaultsAsync(FleetAssetKind assetKind, Guid assetId, Guid? categoryId, int currentOdometerKm,
        CancellationToken cancellationToken);
}

public class MaintenancePolicyService : IMaintenancePolicyService
{
    private const string EntityType = "MaintenancePolicy";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public MaintenancePolicyService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    private IQueryable<MaintenancePolicy> TenantScoped() =>
        _dbContext.Set<MaintenancePolicy>().Where(p => p.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<MaintenancePolicyDto>> ListAsync(CancellationToken cancellationToken)
    {
        var policies = await TenantScoped().AsNoTracking()
            .OrderBy(p => p.AssetKind).ThenBy(p => p.Kind)
            .ThenBy(p => p.VehicleId == null && p.TrailerId == null && p.CategoryId == null ? 0 : p.CategoryId != null ? 1 : 2)
            .ToListAsync(cancellationToken);

        var result = new List<MaintenancePolicyDto>(policies.Count);
        foreach (var policy in policies)
        {
            result.Add(await MapAsync(policy, cancellationToken));
        }

        return result;
    }

    public async Task<MaintenancePolicyDto> CreateAsync(SaveMaintenancePolicyRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);

        var policy = new MaintenancePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
        };
        Apply(policy, request);

        _dbContext.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, policy.Id.ToString(), "Created", null,
            new { policy.Kind, policy.AssetKind, policy.CategoryId, policy.VehicleId, policy.TrailerId, policy.IntervalMonths, policy.IntervalKm }, cancellationToken);

        return await MapAsync(policy, cancellationToken);
    }

    public async Task<MaintenancePolicyDto?> UpdateAsync(Guid id, SaveMaintenancePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await TenantScoped().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            return null;
        }

        await ValidateAsync(request, cancellationToken);

        var before = new { policy.Kind, policy.AssetKind, policy.IntervalMonths, policy.IntervalKm, policy.WarningDays, policy.IsActive };
        Apply(policy, request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, policy.Id.ToString(), "Updated", before,
            new { policy.Kind, policy.AssetKind, policy.IntervalMonths, policy.IntervalKm, policy.WarningDays, policy.IsActive }, cancellationToken);

        return await MapAsync(policy, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var policy = await TenantScoped().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            return false;
        }

        _dbContext.Remove(policy); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, policy.Id.ToString(), "Deleted",
            new { policy.Kind, policy.AssetKind }, null, cancellationToken);

        return true;
    }

    public async Task<ResolvedMaintenancePolicy?> ResolveAsync(
        MaintenancePolicyKind kind, FleetAssetKind assetKind, Guid assetId, Guid? categoryId,
        CancellationToken cancellationToken)
    {
        var candidates = await TenantScoped().AsNoTracking()
            .Where(p => p.IsActive && p.Kind == kind && p.AssetKind == assetKind)
            .ToListAsync(cancellationToken);

        var assetMatch = candidates.FirstOrDefault(p =>
            assetKind == FleetAssetKind.Vehicle ? p.VehicleId == assetId : p.TrailerId == assetId);
        if (assetMatch is not null)
        {
            return ToResolved(assetMatch, MaintenancePolicyLevel.Asset);
        }

        if (categoryId is { } cat && candidates.FirstOrDefault(p => p.CategoryId == cat) is { } categoryMatch)
        {
            return ToResolved(categoryMatch, MaintenancePolicyLevel.Category);
        }

        var companyDefault = candidates.FirstOrDefault(p =>
            p.CategoryId is null && p.VehicleId is null && p.TrailerId is null);
        return companyDefault is null ? null : ToResolved(companyDefault, MaintenancePolicyLevel.CompanyDefault);
    }

    public async Task ApplyDefaultsAsync(FleetAssetKind assetKind, Guid assetId, Guid? categoryId, int currentOdometerKm,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var created = false;

        var maintenance = await ResolveAsync(MaintenancePolicyKind.Maintenance, assetKind, assetId, categoryId, cancellationToken);
        if (maintenance is not null
            && (maintenance.IntervalMonths is not null || (assetKind == FleetAssetKind.Vehicle && maintenance.IntervalKm is not null))
            && !await _dbContext.MaintenanceRecords.AnyAsync(m =>
                m.TenantId == _tenantContext.TenantId
                && (assetKind == FleetAssetKind.Vehicle ? m.VehicleId == assetId : m.TrailerId == assetId)
                && (m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress), cancellationToken))
        {
            _dbContext.MaintenanceRecords.Add(new MaintenanceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                VehicleId = assetKind == FleetAssetKind.Vehicle ? assetId : null,
                TrailerId = assetKind == FleetAssetKind.Trailer ? assetId : null,
                MaintenanceType = MaintenanceType.PeriodicService,
                Status = MaintenanceStatus.Planned,
                Description = "Periodiek onderhoud (volgens onderhoudsbeleid)",
                ScheduledDate = maintenance.IntervalMonths is { } months ? today.AddMonths(months) : null,
                OdometerTriggerKm = assetKind == FleetAssetKind.Vehicle && maintenance.IntervalKm is { } km
                    ? currentOdometerKm + km
                    : null,
                IntervalMonths = maintenance.IntervalMonths,
                IntervalKm = assetKind == FleetAssetKind.Vehicle ? maintenance.IntervalKm : null,
            });
            created = true;
        }

        // Inspections are date-driven; a km-only policy has insufficient data and is skipped.
        var inspection = await ResolveAsync(MaintenancePolicyKind.Inspection, assetKind, assetId, categoryId, cancellationToken);
        if (inspection?.IntervalMonths is { } inspectionMonths
            && !await _dbContext.Inspections.AnyAsync(i =>
                i.TenantId == _tenantContext.TenantId
                && (assetKind == FleetAssetKind.Vehicle ? i.VehicleId == assetId : i.TrailerId == assetId)
                && i.CompletedDate == null, cancellationToken))
        {
            _dbContext.Inspections.Add(new Inspection
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                VehicleId = assetKind == FleetAssetKind.Vehicle ? assetId : null,
                TrailerId = assetKind == FleetAssetKind.Trailer ? assetId : null,
                InspectionType = assetKind == FleetAssetKind.Vehicle ? InspectionType.VehicleInspection : InspectionType.TrailerInspection,
                DueDate = today.AddMonths(inspectionMonths),
                IntervalMonths = inspectionMonths,
                WarningDays = inspection.WarningDays,
                Notes = "Ingepland volgens onderhoudsbeleid",
            });
            created = true;
        }

        if (created)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, assetId.ToString(), "DefaultsApplied", null,
                new { AssetKind = assetKind.ToString(), MaintenancePlanned = maintenance is not null, InspectionPlanned = inspection is not null }, cancellationToken);
        }
    }

    private static ResolvedMaintenancePolicy ToResolved(MaintenancePolicy policy, MaintenancePolicyLevel level) =>
        new(policy.Id, policy.IntervalMonths, policy.IntervalKm, policy.WarningDays, level);

    private static void Apply(MaintenancePolicy policy, SaveMaintenancePolicyRequest request)
    {
        policy.Kind = request.Kind;
        policy.AssetKind = request.AssetKind;
        policy.CategoryId = request.CategoryId;
        policy.VehicleId = request.VehicleId;
        policy.TrailerId = request.TrailerId;
        policy.IntervalMonths = request.IntervalMonths;
        policy.IntervalKm = request.IntervalKm;
        policy.WarningDays = request.WarningDays;
        policy.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        policy.IsActive = request.IsActive;
    }

    private async Task ValidateAsync(SaveMaintenancePolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.IntervalMonths is null && request.IntervalKm is null)
        {
            throw new DomainValidationException("intervalMonths", "Geef een interval in maanden en/of kilometers op.");
        }

        if (request.IntervalMonths is <= 0)
        {
            throw new DomainValidationException("intervalMonths", "Interval in maanden moet groter dan nul zijn.");
        }

        if (request.IntervalKm is <= 0)
        {
            throw new DomainValidationException("intervalKm", "Interval in kilometers moet groter dan nul zijn.");
        }

        if (request.IntervalKm is not null && request.AssetKind != FleetAssetKind.Vehicle)
        {
            throw new DomainValidationException("intervalKm", "Een kilometerinterval geldt alleen voor voertuigen.");
        }

        if (request.WarningDays is < 0 or > 365)
        {
            throw new DomainValidationException("warningDays", "Waarschuwingstermijn moet tussen 0 en 365 dagen liggen.");
        }

        var levels = (request.VehicleId is not null ? 1 : 0) + (request.TrailerId is not null ? 1 : 0) + (request.CategoryId is not null ? 1 : 0);
        if (levels > 1)
        {
            throw new DomainValidationException("Een regel geldt voor één niveau: een specifiek voertuig/oplegger, een categorie, óf als bedrijfsstandaard.");
        }

        if (request.VehicleId is not null && request.AssetKind != FleetAssetKind.Vehicle)
        {
            throw new DomainValidationException("vehicleId", "Een voertuigregel vereist het activatype Voertuig.");
        }

        if (request.TrailerId is not null && request.AssetKind != FleetAssetKind.Trailer)
        {
            throw new DomainValidationException("trailerId", "Een opleggerregel vereist het activatype Oplegger.");
        }

        var tenantId = _tenantContext.TenantId;
        if (request.VehicleId is { } vehicleId
            && !await _dbContext.Vehicles.AnyAsync(v => v.Id == vehicleId && v.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("voertuig");
        }

        if (request.TrailerId is { } trailerId
            && !await _dbContext.Trailers.AnyAsync(t => t.Id == trailerId && t.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("oplegger");
        }

        if (request.CategoryId is { } categoryId)
        {
            var known = request.AssetKind == FleetAssetKind.Vehicle
                ? await _dbContext.VehicleCategories.AnyAsync(c => c.Id == categoryId && c.TenantId == tenantId, cancellationToken)
                : await _dbContext.TrailerCategories.AnyAsync(c => c.Id == categoryId && c.TenantId == tenantId, cancellationToken);
            if (!known)
            {
                throw new InvalidTenantReferenceException("categorie");
            }
        }
    }

    private async Task<MaintenancePolicyDto> MapAsync(MaintenancePolicy policy, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        string? categoryName = null;
        if (policy.CategoryId is { } categoryId)
        {
            categoryName = policy.AssetKind == FleetAssetKind.Vehicle
                ? await _dbContext.VehicleCategories.Where(c => c.Id == categoryId && c.TenantId == tenantId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
                : await _dbContext.TrailerCategories.Where(c => c.Id == categoryId && c.TenantId == tenantId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        }

        var vehicleNumber = policy.VehicleId is { } vehicleId
            ? await _dbContext.Vehicles.Where(v => v.Id == vehicleId && v.TenantId == tenantId).Select(v => v.InternalNumber).FirstOrDefaultAsync(cancellationToken)
            : null;
        var trailerNumber = policy.TrailerId is { } trailerId
            ? await _dbContext.Trailers.Where(t => t.Id == trailerId && t.TenantId == tenantId).Select(t => t.InternalNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new MaintenancePolicyDto(
            policy.Id, policy.Kind, policy.AssetKind,
            policy.CategoryId, categoryName,
            policy.VehicleId, vehicleNumber,
            policy.TrailerId, trailerNumber,
            policy.IntervalMonths, policy.IntervalKm, policy.WarningDays,
            policy.Description, policy.IsActive);
    }
}
