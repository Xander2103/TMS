using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Operations.Dtos;
using TransportationService.Api.Modules.Operations.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Operations.Services;

public class AlertService : IAlertService
{
    private const string EntityType = "OperationalAlert";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public AlertService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    private IQueryable<OperationalAlert> TenantScoped() =>
        _dbContext.OperationalAlerts.Where(a => a.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<OperationalAlertDto>> ListAsync(AlertQuery query, CancellationToken cancellationToken)
    {
        var alerts = TenantScoped().AsNoTracking();

        if (query.Status is { } status) alerts = alerts.Where(a => a.Status == status);
        if (query.Severity is { } severity) alerts = alerts.Where(a => a.Severity == severity);
        if (!string.IsNullOrWhiteSpace(query.Category)) alerts = alerts.Where(a => a.Category == query.Category);

        var limit = Math.Clamp(query.Limit, 1, 500);
        // Severity is stored as a string; an explicit rank keeps Critical first in SQL.
        var rows = await alerts
            .OrderByDescending(a => a.Severity == AlertSeverity.Critical ? 2 : a.Severity == AlertSeverity.Warning ? 1 : 0)
            .ThenByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var userIds = rows
            .SelectMany(a => new[] { a.AssignedUserId, a.AcknowledgedByUserId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var users = userIds.Count == 0
            ? []
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId && userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, cancellationToken);

        return rows.Select(a => Map(a, users)).ToList();
    }

    public async Task<OperationalAlertDto?> AcknowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var alert = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        if (alert.Status == AlertStatus.Active)
        {
            alert.Status = AlertStatus.Acknowledged;
            alert.AcknowledgedByUserId = _currentUserContext.CurrentUserId;
            alert.AcknowledgedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, alert.Id.ToString(), "Acknowledged",
                null, new { alert.DedupeKey, alert.Title }, cancellationToken);
        }

        return await MapSingleAsync(alert, cancellationToken);
    }

    public async Task<OperationalAlertDto?> ResolveAsync(Guid id, CancellationToken cancellationToken)
    {
        var alert = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        if (alert.Status != AlertStatus.Resolved)
        {
            alert.Status = AlertStatus.Resolved;
            alert.ResolvedByUserId = _currentUserContext.CurrentUserId;
            alert.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, alert.Id.ToString(), "Resolved",
                null, new { alert.DedupeKey, alert.Title }, cancellationToken);
        }

        return await MapSingleAsync(alert, cancellationToken);
    }

    public async Task<OperationalAlertDto?> AssignAsync(Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var alert = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        if (userId is { } assignee && !await _dbContext.Users.AnyAsync(
                u => u.Id == assignee && u.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            throw new Common.DomainValidationException("De toegewezen gebruiker bestaat niet.");
        }

        var before = new { alert.AssignedUserId };
        alert.AssignedUserId = userId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, alert.Id.ToString(), "Assigned",
            before, new { alert.AssignedUserId }, cancellationToken);

        return await MapSingleAsync(alert, cancellationToken);
    }

    private async Task<OperationalAlertDto> MapSingleAsync(OperationalAlert alert, CancellationToken cancellationToken)
    {
        var userIds = new[] { alert.AssignedUserId, alert.AcknowledgedByUserId }
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? []
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId && userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, cancellationToken);
        return Map(alert, users);
    }

    private static OperationalAlertDto Map(OperationalAlert a, IReadOnlyDictionary<Guid, string> users) => new(
        a.Id, a.Severity, a.Category, a.Source, a.Title, a.Message, a.LinkPath,
        a.RelatedEntityType, a.RelatedEntityId, a.Status,
        a.AssignedUserId, a.AssignedUserId is { } au ? users.GetValueOrDefault(au) : null,
        a.AcknowledgedByUserId, a.AcknowledgedByUserId is { } ab ? users.GetValueOrDefault(ab) : null,
        a.AcknowledgedAt, a.ResolvedAt, a.CreatedAt, a.LastSeenAt);
}
