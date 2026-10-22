using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Dtos;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Auditing.Controllers;

[ApiController]
[Route("api/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly TransportationDbContext _dbContext;
    private readonly IAuditHistoryReader _reader;

    public AuditLogsController(TransportationDbContext dbContext, IAuditHistoryReader reader)
    {
        _dbContext = dbContext;
        _reader = reader;
    }

    /// <summary>
    /// One page of the tenant's audit log, newest first. PERF-H5: <paramref name="pageSize"/> is
    /// clamped to <see cref="PageRequest.MaxPageSize"/> (200) and defaults to 25 — the response's
    /// <c>pageSize</c> says what was applied; <c>totalCount</c> stays the full count.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.AuditLogsView)]
    public async Task<ActionResult<AuditLogPagedResult>> List(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var paging = PageRequest.Of(page, pageSize);
        var rows = _dbContext.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            rows = rows.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            rows = rows.Where(a => a.EntityId == entityId);
        }

        return Ok(await _reader.ReadAsync(rows, paging.Page, paging.PageSize, redactedKeys: null, cancellationToken));
    }
}
