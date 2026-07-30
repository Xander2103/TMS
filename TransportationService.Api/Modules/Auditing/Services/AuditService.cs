using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Auditing.Services;

/// <summary>
/// Never pass raw entities containing PasswordHash or document bytes into
/// oldValues/newValues — callers pass purpose-built anonymous objects with
/// only the fields worth auditing (see call sites for the pattern).
///
/// Every record is stamped with the request's client IP and correlation id so an audit trail is
/// usable for forensics (who, from where, in which request). The IP is taken from
/// <c>HttpContext.Connection.RemoteIpAddress</c> AFTER <c>UseForwardedHeaders</c> has resolved it
/// from a known proxy, so it is the real client address rather than the proxy's.
/// </summary>
public class AuditService : IAuditService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AuditService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task RecordAsync(string entityType, string entityId, string action, object? oldValues, object? newValues, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor?.HttpContext;

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUserContext.CurrentUserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValuesJson = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValuesJson = newValues is null ? null : JsonSerializer.Serialize(newValues),
            Timestamp = DateTime.UtcNow,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = httpContext?.TraceIdentifier,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
