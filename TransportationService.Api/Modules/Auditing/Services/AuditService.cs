using System.Text.Json;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Auditing.Services;

/// <summary>
/// Never pass raw entities containing PasswordHash or document bytes into
/// oldValues/newValues — callers pass purpose-built anonymous objects with
/// only the fields worth auditing (see call sites for the pattern).
/// </summary>
public class AuditService : IAuditService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public AuditService(TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
    }

    public async Task RecordAsync(string entityType, string entityId, string action, object? oldValues, object? newValues, CancellationToken cancellationToken)
    {
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
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
