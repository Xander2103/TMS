using Microsoft.AspNetCore.Mvc;
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
    private const int DefaultPageSize = 25;

    private readonly TransportationDbContext _dbContext;
    private readonly IAuditHistoryReader _reader;

    public AuditLogsController(TransportationDbContext dbContext, IAuditHistoryReader reader)
    {
        _dbContext = dbContext;
        _reader = reader;
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
        var rows = _dbContext.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            rows = rows.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            rows = rows.Where(a => a.EntityId == entityId);
        }

        return Ok(await _reader.ReadAsync(rows, page, pageSize, redactedKeys: null, cancellationToken));
    }
}
