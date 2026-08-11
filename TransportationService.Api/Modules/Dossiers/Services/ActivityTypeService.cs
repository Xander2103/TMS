using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IActivityTypeService
{
    Task<IReadOnlyList<ActivityTypeDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<ActivityTypeDto> CreateAsync(SaveActivityTypeRequest request, CancellationToken cancellationToken);

    Task<ActivityTypeDto?> UpdateAsync(Guid id, SaveActivityTypeRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete; false when the id does not exist for this tenant.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public class ActivityTypeService : IActivityTypeService
{
    private const string EntityType = "ActivityType";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IActivityTypeSeeder _seeder;
    private readonly IAuditService _auditService;

    public ActivityTypeService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IActivityTypeSeeder seeder,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _seeder = seeder;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<ActivityTypeDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        // Lazy per-tenant seed on first read (AccountingService pattern): the settings page
        // and every picker call List, so the default catalogue exists before it is needed.
        await _seeder.EnsureSeededAsync(cancellationToken);

        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.ActivityTypes.AsNoTracking().Where(t => t.TenantId == tenantId);
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        var types = await query
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return types.Select(ToDto).ToList();
    }

    public async Task<ActivityTypeDto> CreateAsync(SaveActivityTypeRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        Validate(request);

        var code = request.Code.Trim();
        var duplicate = await _dbContext.ActivityTypes
            .AnyAsync(t => t.TenantId == tenantId && t.Code.ToLower() == code.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", "Er bestaat al een activiteitstype met deze code.");
        }

        if (request.IsSystemDefaultTransport)
        {
            RequireActiveForDefaultFlag(request);
            await ClearCurrentDefaultTransportAsync(tenantId, exceptId: null, cancellationToken);
        }

        var type = new ActivityType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
        };
        Apply(type, request);
        _dbContext.Add(type);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, type.Id.ToString(), "Created", null,
            new { type.Code, type.Name, type.IsActive, type.IsSystemDefaultTransport }, cancellationToken);

        return ToDto(type);
    }

    public async Task<ActivityTypeDto?> UpdateAsync(Guid id, SaveActivityTypeRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var type = await FindAsync(id, cancellationToken);
        if (type is null)
        {
            return null;
        }

        Validate(request);

        if (!string.Equals(request.Code.Trim(), type.Code, StringComparison.Ordinal))
        {
            throw new DomainValidationException("code", "De code van een activiteitstype kan niet gewijzigd worden.");
        }

        var isCurrentDefault = type.IsSystemDefaultTransport && type.IsActive;
        var staysDefault = request.IsSystemDefaultTransport && request.IsActive;
        if (isCurrentDefault && !staysDefault)
        {
            // The filtered unique index guarantees no second active carrier can exist while
            // this one holds the flag, so releasing it here always leaves zero — the flag
            // moves by assigning it to ANOTHER type (clear-then-set below), never by
            // deactivating or unflagging the current default.
            await RequireAnotherActiveDefaultAsync(tenantId, id, cancellationToken);
        }

        if (request.IsSystemDefaultTransport)
        {
            RequireActiveForDefaultFlag(request);
            if (!type.IsSystemDefaultTransport)
            {
                // Clear-then-set in two SaveChanges: the filtered unique index is checked
                // per statement, so the old carrier must be released before the new one
                // takes the flag.
                await ClearCurrentDefaultTransportAsync(tenantId, exceptId: id, cancellationToken);
            }
        }

        Apply(type, request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, type.Id.ToString(), "Updated", null,
            new { type.Code, type.Name, type.IsActive, type.IsSystemDefaultTransport }, cancellationToken);

        return ToDto(type);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var type = await FindAsync(id, cancellationToken);
        if (type is null)
        {
            return false;
        }

        if (type.IsSystemDefaultTransport && type.IsActive)
        {
            await RequireAnotherActiveDefaultAsync(tenantId, id, cancellationToken);
        }

        var inUse = await _dbContext.DossierActivities
            .AnyAsync(a => a.TenantId == tenantId && a.ActivityTypeId == id, cancellationToken);
        if (inUse)
        {
            throw new DomainValidationException("Dit activiteitstype is in gebruik en kan niet verwijderd worden.");
        }

        _dbContext.Remove(type); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, type.Id.ToString(), "Deleted", null,
            new { type.Code, type.Name }, cancellationToken);

        return true;
    }

    private static void Validate(SaveActivityTypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new DomainValidationException("code", "Een code is verplicht.");
        }

        if (request.Code.Trim().Length > 50)
        {
            throw new DomainValidationException("code", "De code mag maximaal 50 tekens lang zijn.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "Een naam is verplicht.");
        }

        if (request.Name.Trim().Length > 100)
        {
            throw new DomainValidationException("name", "De naam mag maximaal 100 tekens lang zijn.");
        }

        if (request.Icon is { } icon && icon.Trim().Length > 50)
        {
            throw new DomainValidationException("icon", "Het icoon mag maximaal 50 tekens lang zijn.");
        }

        if (request.KpiCategory is { } kpi && kpi.Trim().Length > 50)
        {
            throw new DomainValidationException("kpiCategory", "De KPI-categorie mag maximaal 50 tekens lang zijn.");
        }
    }

    private static void RequireActiveForDefaultFlag(SaveActivityTypeRequest request)
    {
        if (!request.IsActive)
        {
            throw new DomainValidationException(
                "isSystemDefaultTransport", "Alleen een actief activiteitstype kan het standaard transporttype zijn.");
        }
    }

    private async Task RequireAnotherActiveDefaultAsync(Guid tenantId, Guid exceptId, CancellationToken cancellationToken)
    {
        var anotherExists = await _dbContext.ActivityTypes.AnyAsync(
            t => t.TenantId == tenantId && t.Id != exceptId && t.IsActive && t.IsSystemDefaultTransport,
            cancellationToken);
        if (!anotherExists)
        {
            throw new DomainValidationException("Er moet altijd één actief standaard transporttype zijn.");
        }
    }

    private async Task ClearCurrentDefaultTransportAsync(Guid tenantId, Guid? exceptId, CancellationToken cancellationToken)
    {
        var current = await _dbContext.ActivityTypes
            .Where(t => t.TenantId == tenantId && t.IsSystemDefaultTransport && (exceptId == null || t.Id != exceptId))
            .ToListAsync(cancellationToken);
        if (current.Count == 0)
        {
            return;
        }

        foreach (var carrier in current)
        {
            carrier.IsSystemDefaultTransport = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(ActivityType type, SaveActivityTypeRequest request)
    {
        type.Name = request.Name.Trim();
        type.IsActive = request.IsActive;
        type.SortOrder = request.SortOrder;
        type.Icon = Trim(request.Icon);
        type.KpiCategory = Trim(request.KpiCategory);
        type.HasStops = request.HasStops;
        type.SupportsGoods = request.SupportsGoods;
        type.PlanningRelevant = request.PlanningRelevant;
        type.WarehouseRelevant = request.WarehouseRelevant;
        type.AllowsDuration = request.AllowsDuration;
        type.IsQuickStart = request.IsQuickStart;
        type.QuickStartOrder = request.QuickStartOrder;
        type.IsSystemDefaultTransport = request.IsSystemDefaultTransport;
    }

    private async Task<ActivityType?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.ActivityTypes
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);

    private static ActivityTypeDto ToDto(ActivityType t) => new(
        t.Id, t.Code, t.Name, t.IsActive, t.SortOrder, t.Icon, t.KpiCategory,
        t.HasStops, t.SupportsGoods, t.PlanningRelevant, t.WarehouseRelevant,
        t.AllowsDuration, t.IsQuickStart, t.QuickStartOrder, t.IsSystemDefaultTransport);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
