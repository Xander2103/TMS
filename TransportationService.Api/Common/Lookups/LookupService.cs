using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// Single generic implementation of <see cref="ILookupService{TEntity}"/> covering every
/// lookup table. Tenant scoping is applied on every query; code uniqueness is enforced both
/// here (for friendly errors) and by a unique index (as the concurrency backstop).
/// </summary>
public class LookupService<TEntity> : ILookupService<TEntity> where TEntity : LookupEntity, new()
{
    private static readonly string EntityTypeName = typeof(TEntity).Name;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public LookupService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<TEntity> TenantScoped() =>
        _dbContext.Set<TEntity>().Where(e => e.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<LookupItemDto>> SearchAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (isActive is { } activeFilter)
        {
            query = query.Where(e => e.IsActive == activeFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(e =>
                EF.Functions.Like(e.Code, pattern) ||
                EF.Functions.Like(e.Name, pattern));
        }

        return await query
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
            .ToPagedResultAsync(page, e => MapToDto(e), cancellationToken);
    }

    public async Task<IReadOnlyList<LookupOptionDto>> ListOptionsAsync(CancellationToken cancellationToken) =>
        await TenantScoped()
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
            .Select(e => new LookupOptionDto(e.Id, e.Code, e.Name))
            .ToListAsync(cancellationToken);

    public async Task<LookupItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<LookupOperationResult> CreateAsync(CreateLookupRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Code, request.Name) is { } validationError)
        {
            return LookupOperationResult.Invalid(validationError);
        }

        var code = request.Code.Trim();
        if (await CodeExistsAsync(code, excludingId: null, cancellationToken))
        {
            return LookupOperationResult.Duplicate(code);
        }

        var entity = new TEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Code = code,
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
        };

        _dbContext.Set<TEntity>().Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LookupOperationResult.Duplicate(code);
        }

        await _auditService.RecordAsync(EntityTypeName, entity.Id.ToString(), "Created", null,
            new { entity.Code, entity.Name, entity.IsActive }, cancellationToken);

        return LookupOperationResult.Ok(MapToDto(entity));
    }

    public async Task<LookupOperationResult> UpdateAsync(Guid id, UpdateLookupRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Code, request.Name) is { } validationError)
        {
            return LookupOperationResult.Invalid(validationError);
        }

        var entity = await TenantScoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return LookupOperationResult.NotFound();
        }

        var code = request.Code.Trim();
        if (await CodeExistsAsync(code, excludingId: id, cancellationToken))
        {
            return LookupOperationResult.Duplicate(code);
        }

        var oldValues = new { entity.Code, entity.Name, entity.IsActive };

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.Description = Normalize(request.Description);
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LookupOperationResult.Duplicate(code);
        }

        await _auditService.RecordAsync(EntityTypeName, entity.Id.ToString(), "Updated", oldValues,
            new { entity.Code, entity.Name, entity.IsActive }, cancellationToken);

        return LookupOperationResult.Ok(MapToDto(entity));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await TenantScoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        // Interceptor converts this Remove into a soft delete.
        _dbContext.Set<TEntity>().Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityTypeName, entity.Id.ToString(), "Deleted",
            new { entity.Code, entity.Name }, null, cancellationToken);

        return true;
    }

    private Task<bool> CodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) =>
        TenantScoped().AnyAsync(
            e => e.Code.ToLower() == code.ToLower() && (excludingId == null || e.Id != excludingId),
            cancellationToken);

    private static string? Validate(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Code is verplicht.";
        if (string.IsNullOrWhiteSpace(name)) return "Naam is verplicht.";
        return null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LookupItemDto MapToDto(TEntity e) =>
        new(e.Id, e.Code, e.Name, e.Description, e.IsActive, e.SortOrder, e.CreatedAt, e.UpdatedAt);
}
