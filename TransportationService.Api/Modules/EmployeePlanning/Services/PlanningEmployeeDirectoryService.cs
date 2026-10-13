using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

/// <summary>
/// The GLOBAL employee directory of the personnel planning: who can be found and what the
/// filter vocabulary is. Deliberately a separate concern from the schedule query — the active
/// planning filters must never narrow who the planner can search for. Tenant scoping is the
/// only boundary here; the controller adds the planning permission.
/// </summary>
public interface IPlanningEmployeeDirectoryService
{
    /// <summary>
    /// Type-ahead over ALL employees of the tenant (active first, inactive still findable and
    /// flagged). Every word of the term must match number, first name or last name, in any order
    /// ("peeters jan"). Bounded by <paramref name="limit"/> (1–50).
    /// </summary>
    Task<IReadOnlyList<PlanningEmployeeOptionDto>> SearchAsync(string? term, int limit, CancellationToken cancellationToken);

    /// <summary>Names of the lookup values the toolbar filters on, plus the caller's own department.</summary>
    Task<PlanningFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken);
}

public class PlanningEmployeeDirectoryService : IPlanningEmployeeDirectoryService
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public PlanningEmployeeDirectoryService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
    }

    public async Task<IReadOnlyList<PlanningEmployeeOptionDto>> SearchAsync(
        string? term, int limit, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var take = Math.Clamp(limit, 1, MaxLimit);

        var query = _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId);

        // Case-insensitive on PostgreSQL and SQLite alike (plain LIKE is case-sensitive on PostgreSQL).
        var words = (term ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant())
            .Take(5)
            .ToList();
        foreach (var word in words)
        {
            query = query.Where(e =>
                e.EmployeeNumber.ToLower().Contains(word)
                || e.FirstName.ToLower().Contains(word)
                || e.LastName.ToLower().Contains(word));
        }

        var hits = await query
            .OrderByDescending(e => e.IsActive)
            .ThenBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Take(take)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.IsActive,
                DepartmentName = _dbContext.Departments
                    .Where(d => d.Id == e.DepartmentId && d.TenantId == tenantId)
                    .Select(d => d.Name)
                    .FirstOrDefault(),
                FunctionNames = e.JobFunctions
                    .Where(j => j.JobFunction != null)
                    .OrderBy(j => j.JobFunction!.SortOrder).ThenBy(j => j.JobFunction!.Name)
                    .Select(j => j.JobFunction!.Name)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return hits
            .Select(h => new PlanningEmployeeOptionDto(
                h.Id, h.EmployeeNumber, h.FirstName, h.LastName, h.DepartmentName, h.FunctionNames, h.IsActive))
            .ToList();
    }

    public async Task<PlanningFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var departments = await _dbContext.Departments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new PlanningFilterOptionDto(d.Id, d.Name))
            .ToListAsync(cancellationToken);
        var jobFunctions = await _dbContext.JobFunctions.AsNoTracking()
            .Where(j => j.TenantId == tenantId && j.IsActive)
            .OrderBy(j => j.SortOrder).ThenBy(j => j.Name)
            .Select(j => new PlanningFilterOptionDto(j.Id, j.Name))
            .ToListAsync(cancellationToken);
        var contractTypes = await _dbContext.ContractTypes.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new PlanningFilterOptionDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);

        Guid? myDepartmentId = null;
        if (_currentUserContext.CurrentUserId is { } userId)
        {
            // "Mijn afdeling": the department of the caller's own employee record, if linked.
            myDepartmentId = await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.TenantId == tenantId && u.EmployeeId != null)
                .Join(_dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId),
                    u => u.EmployeeId, e => e.Id, (u, e) => e.DepartmentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var hasDrivers = await _dbContext.Drivers.AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId, cancellationToken);

        return new PlanningFilterOptionsDto(departments, jobFunctions, contractTypes, myDepartmentId, hasDrivers);
    }
}
