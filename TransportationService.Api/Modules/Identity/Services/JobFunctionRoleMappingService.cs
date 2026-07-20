using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

public record JobFunctionRoleMappingDto(Guid Id, Guid JobFunctionId, string JobFunctionName, Guid RoleId, string RoleName);

public record SaveJobFunctionRoleMappingRequest(Guid JobFunctionId, Guid RoleId);

public record SuggestedRoleDto(Guid RoleId, string RoleName, string ViaFunction);

public interface IJobFunctionRoleMappingService
{
    Task<IReadOnlyList<JobFunctionRoleMappingDto>> ListAsync(CancellationToken cancellationToken);

    Task<JobFunctionRoleMappingDto> CreateAsync(SaveJobFunctionRoleMappingRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Roles the configured mappings SUGGEST for an employee's functions — never auto-applied.</summary>
    Task<IReadOnlyList<SuggestedRoleDto>> SuggestForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
}

public class JobFunctionRoleMappingService : IJobFunctionRoleMappingService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public JobFunctionRoleMappingService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<JobFunctionRoleMappingDto>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        return await _dbContext.JobFunctionRoleMappings.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Join(_dbContext.JobFunctions.AsNoTracking().Where(f => f.TenantId == tenantId),
                m => m.JobFunctionId, f => f.Id, (m, f) => new { m, FunctionName = f.Name })
            .Join(_dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId),
                x => x.m.RoleId, r => r.Id,
                (x, r) => new JobFunctionRoleMappingDto(x.m.Id, x.m.JobFunctionId, x.FunctionName, x.m.RoleId, r.Name))
            .OrderBy(dto => dto.JobFunctionName).ThenBy(dto => dto.RoleName)
            .ToListAsync(cancellationToken);
    }

    public async Task<JobFunctionRoleMappingDto> CreateAsync(
        SaveJobFunctionRoleMappingRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var functionName = await _dbContext.JobFunctions.AsNoTracking()
            .Where(f => f.Id == request.JobFunctionId && f.TenantId == tenantId)
            .Select(f => f.Name).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidTenantReferenceException("functie");
        var roleName = await _dbContext.Roles.AsNoTracking()
            .Where(r => r.Id == request.RoleId && r.TenantId == tenantId)
            .Select(r => r.Name).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidTenantReferenceException("rol");

        if (await _dbContext.JobFunctionRoleMappings.AnyAsync(m =>
                m.TenantId == tenantId && m.JobFunctionId == request.JobFunctionId && m.RoleId == request.RoleId,
                cancellationToken))
        {
            throw new DomainValidationException("Deze functie is al aan deze rol gekoppeld.");
        }

        var mapping = new JobFunctionRoleMapping
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            JobFunctionId = request.JobFunctionId,
            RoleId = request.RoleId,
            CreatedAt = DateTime.UtcNow,
        };
        _dbContext.JobFunctionRoleMappings.Add(mapping);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("JobFunctionRoleMapping", mapping.Id.ToString(), "Created", null,
            new { FunctionName = functionName, RoleName = roleName }, cancellationToken);

        return new JobFunctionRoleMappingDto(mapping.Id, mapping.JobFunctionId, functionName, mapping.RoleId, roleName);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var mapping = await _dbContext.JobFunctionRoleMappings
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        if (mapping is null)
        {
            return false;
        }

        _dbContext.JobFunctionRoleMappings.Remove(mapping);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("JobFunctionRoleMapping", id.ToString(), "Deleted", null, null, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SuggestedRoleDto>> SuggestForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var functionIds = await _dbContext.Set<Modules.Employees.Entities.EmployeeJobFunction>().AsNoTracking()
            .Where(j => j.EmployeeId == employeeId)
            .Select(j => j.JobFunctionId)
            .ToListAsync(cancellationToken);
        if (functionIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.JobFunctionRoleMappings.AsNoTracking()
            .Where(m => m.TenantId == tenantId && functionIds.Contains(m.JobFunctionId))
            .Join(_dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive),
                m => m.RoleId, r => r.Id, (m, r) => new { m.JobFunctionId, r.Id, r.Name })
            .Join(_dbContext.JobFunctions.AsNoTracking(), x => x.JobFunctionId, f => f.Id,
                (x, f) => new SuggestedRoleDto(x.Id, x.Name, f.Name))
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
