using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Dtos;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Auditing.Controllers;

[ApiController]
[Route("api/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private const int DefaultPageSize = 25;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditReferenceResolver _references;

    public AuditLogsController(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditReferenceResolver references)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _references = references;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.AuditLogsView)]
    public async Task<ActionResult<AuditLogPagedResult>> List(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : pageSize;

        var query = _dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId);

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(a => a.EntityId == entityId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.UserId, a.EntityType, a.EntityId, a.Action, a.OldValuesJson, a.NewValuesJson, a.Timestamp))
            .ToListAsync(cancellationToken);

        // Actor names (product acceptance 2026-10-07): the history must say WHO confirmed, reopened or
        // cancelled. Resolved within the tenant only; a foreign or deleted user stays nameless.
        var userIds = items.Where(i => i.UserId is not null).Select(i => i.UserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var tenantId = _tenantContext.TenantId;
            var names = await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FirstName + u.LastName) ? u.Email : (u.FirstName + " " + u.LastName).Trim(), cancellationToken);
            items = items.Select(i => i.UserId is { } id && names.TryGetValue(id, out var name) ? i with { UserName = name } : i).ToList();
        }

        // B25: the ids inside the values become references a planner recognises (per entry, only its own ids).
        var resolved = await _references.ResolveAsync(items.SelectMany(i => new[] { i.OldValuesJson, i.NewValuesJson }), cancellationToken);
        if (resolved.Count > 0)
        {
            items = items.Select(i =>
            {
                var own = AuditReferenceResolver.CollectIds([i.OldValuesJson, i.NewValuesJson])
                    .Where(resolved.ContainsKey)
                    .ToDictionary(id => id.ToString(), id => resolved[id]);
                return own.Count > 0 ? i with { References = own } : i;
            }).ToList();
        }

        return Ok(new AuditLogPagedResult(items, totalCount, page, pageSize));
    }
}
