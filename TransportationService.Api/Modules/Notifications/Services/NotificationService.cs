using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Notifications.Services;

public record NotificationDto(
    Guid Id, string Type, string Title, string Message, string? LinkPath, bool IsRead, DateTime CreatedAt);

public interface INotificationService
{
    /// <summary>Queues a notification; silently skipped when the recipient is null (no linked user).</summary>
    Task NotifyAsync(Guid? userId, string type, string title, string message, string? linkPath, CancellationToken cancellationToken);

    /// <summary>Notifies every active user of the tenant that holds the given permission.</summary>
    Task NotifyPermissionHoldersAsync(string permissionCode, string type, string title, string message, string? linkPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDto>> ListMineAsync(bool unreadOnly, int take, CancellationToken cancellationToken);

    Task<int> UnreadCountAsync(CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(Guid id, CancellationToken cancellationToken);

    Task MarkAllReadAsync(CancellationToken cancellationToken);
}

public class NotificationService : INotificationService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly TimeProvider _timeProvider;

    public NotificationService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _timeProvider = timeProvider;
    }

    public async Task NotifyAsync(
        Guid? userId, string type, string title, string message, string? linkPath, CancellationToken cancellationToken)
    {
        if (userId is not { } recipient)
        {
            return;
        }

        _dbContext.Add(new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            UserId = recipient,
            Type = type,
            Title = title,
            Message = message,
            LinkPath = linkPath,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyPermissionHoldersAsync(
        string permissionCode, string type, string title, string message, string? linkPath, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var recipients = await (from ur in _dbContext.UserRoles.AsNoTracking()
                                join u in _dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive)
                                    on ur.UserId equals u.Id
                                join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive)
                                    on ur.RoleId equals r.Id
                                join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
                                join p in _dbContext.Permissions.AsNoTracking().Where(p => p.Code == permissionCode)
                                    on rp.PermissionId equals p.Id
                                select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            _dbContext.Add(new Notification
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = recipient,
                Type = type,
                Title = title,
                Message = message,
                LinkPath = linkPath,
            });
        }

        if (recipients.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private IQueryable<Notification> Mine()
    {
        var userId = _currentUserContext.CurrentUserId ?? Guid.Empty;
        return _dbContext.Notifications
            .Where(n => n.TenantId == _tenantContext.TenantId && n.UserId == userId);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListMineAsync(
        bool unreadOnly, int take, CancellationToken cancellationToken)
    {
        var query = Mine().AsNoTracking();
        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.LinkPath, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken) =>
        Mine().AsNoTracking().CountAsync(n => !n.IsRead, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await Mine().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var unread = await Mine().Where(n => !n.IsRead).ToListAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        if (unread.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
