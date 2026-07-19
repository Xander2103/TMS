using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.TripCosting.Services;

public class CostRateService : ICostRateService
{
    private const string EntityType = "CostRateSet";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public CostRateService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<CostRateSet> Scoped() =>
        _dbContext.CostRateSets.Where(r => r.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<CostRateSetDto>> ListAsync(CancellationToken cancellationToken)
    {
        var sets = await Scoped().AsNoTracking()
            .OrderByDescending(r => r.EffectiveFrom)
            .Take(100)
            .ToListAsync(cancellationToken);
        return sets.Select(Map).ToList();
    }

    public async Task<CostRateSetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var set = await Scoped().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return set is null ? null : Map(set);
    }

    public Task<CostRateSet?> GetForDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Scoped().AsNoTracking()
            .Where(r => r.EffectiveFrom <= date)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(CostRateSetDto? Result, string? Error)> CreateAsync(
        SaveCostRateSetRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error)
        {
            return (null, error);
        }

        if (await Scoped().AnyAsync(r => r.EffectiveFrom == request.EffectiveFrom, cancellationToken))
        {
            return (null, "Er bestaat al een tarievenset met deze ingangsdatum.");
        }

        var set = new CostRateSet { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        Apply(set, request);
        _dbContext.Add(set);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, set.Id.ToString(), "Created", null,
            new { set.EffectiveFrom, set.Name, set.FuelPricePerLitre, set.DriverCostPerHour }, cancellationToken);

        return (Map(set), null);
    }

    public async Task<(CostRateSetDto? Result, string? Error)> UpdateAsync(
        Guid id, SaveCostRateSetRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error)
        {
            return (null, error);
        }

        var set = await Scoped().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (set is null)
        {
            return (null, null);
        }

        if (await Scoped().AnyAsync(r => r.Id != id && r.EffectiveFrom == request.EffectiveFrom, cancellationToken))
        {
            return (null, "Er bestaat al een tarievenset met deze ingangsdatum.");
        }

        var before = new { set.EffectiveFrom, set.FuelPricePerLitre, set.DriverCostPerHour, set.VehicleCostPerKm };
        Apply(set, request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, set.Id.ToString(), "Updated", before,
            new { set.EffectiveFrom, set.FuelPricePerLitre, set.DriverCostPerHour, set.VehicleCostPerKm }, cancellationToken);

        return (Map(set), null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var set = await Scoped().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (set is null)
        {
            return false;
        }

        _dbContext.Remove(set); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, set.Id.ToString(), "Deleted",
            new { set.EffectiveFrom, set.Name }, null, cancellationToken);
        return true;
    }

    private static string? Validate(SaveCostRateSetRequest request)
    {
        var rates = new[]
        {
            request.FuelPricePerLitre, request.DefaultConsumptionLPer100Km, request.VehicleCostPerKm,
            request.VehicleCostPerHour, request.DriverCostPerHour, request.EmployerCostMultiplier,
            request.MaintenanceCostPerKm, request.DepreciationPerDay, request.TrailerCostPerDay,
            request.EquipmentCostPerDay, request.DefaultTollPerTrip, request.OvertimeRateMultiplier,
            request.WaitingTimeCostPerHour, request.Co2KgPerLitreDiesel, request.Co2KgPerLitreOther,
        };
        if (rates.Any(r => r < 0))
        {
            return "Tarieven kunnen niet negatief zijn.";
        }

        if (request.OvertimeThresholdMinutesPerDay is < 0 or > 1440)
        {
            return "De overurengrens moet tussen 0 en 1440 minuten liggen.";
        }

        if (request.EmployerCostMultiplier is < 1 or > 5)
        {
            return "De werkgeverslastenfactor moet tussen 1 en 5 liggen.";
        }

        return null;
    }

    private static void Apply(CostRateSet set, SaveCostRateSetRequest request)
    {
        set.EffectiveFrom = request.EffectiveFrom;
        set.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        set.FuelPricePerLitre = request.FuelPricePerLitre;
        set.DefaultConsumptionLPer100Km = request.DefaultConsumptionLPer100Km;
        set.VehicleCostPerKm = request.VehicleCostPerKm;
        set.VehicleCostPerHour = request.VehicleCostPerHour;
        set.DriverCostPerHour = request.DriverCostPerHour;
        set.EmployerCostMultiplier = request.EmployerCostMultiplier;
        set.MaintenanceCostPerKm = request.MaintenanceCostPerKm;
        set.DepreciationPerDay = request.DepreciationPerDay;
        set.TrailerCostPerDay = request.TrailerCostPerDay;
        set.EquipmentCostPerDay = request.EquipmentCostPerDay;
        set.DefaultTollPerTrip = request.DefaultTollPerTrip;
        set.OvertimeThresholdMinutesPerDay = request.OvertimeThresholdMinutesPerDay;
        set.OvertimeRateMultiplier = request.OvertimeRateMultiplier;
        set.WaitingTimeCostPerHour = request.WaitingTimeCostPerHour;
        set.Co2KgPerLitreDiesel = request.Co2KgPerLitreDiesel;
        set.Co2KgPerLitreOther = request.Co2KgPerLitreOther;
    }

    private static CostRateSetDto Map(CostRateSet r) => new(
        r.Id, r.EffectiveFrom, r.Name, r.FuelPricePerLitre, r.DefaultConsumptionLPer100Km,
        r.VehicleCostPerKm, r.VehicleCostPerHour, r.DriverCostPerHour, r.EmployerCostMultiplier,
        r.MaintenanceCostPerKm, r.DepreciationPerDay, r.TrailerCostPerDay, r.EquipmentCostPerDay,
        r.DefaultTollPerTrip, r.OvertimeThresholdMinutesPerDay, r.OvertimeRateMultiplier,
        r.WaitingTimeCostPerHour, r.Co2KgPerLitreDiesel, r.Co2KgPerLitreOther);
}
