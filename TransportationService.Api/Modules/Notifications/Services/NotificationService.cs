using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Notifications.Services;

public record NotificationDto(
    Guid Id, string Type, NotificationCategory Category, NotificationSeverity Severity,
    string Title, string Message, string? LinkPath, bool IsRead, bool IsArchived, DateTime CreatedAt,
    bool RequiresAcknowledgement = false, DateTime? AcknowledgedAt = null,
    DateTime? ResolvedAt = null, DateTime? ExpiresAt = null);

public record NotificationQuery(bool UnreadOnly, NotificationCategory? Category, bool IncludeArchived, int Take);

/// <summary>
/// Optional lifecycle behaviour for a produced notification. DedupeKey suppresses the insert
/// while an unresolved notification with the same key exists for the recipient; resolving
/// (stock recovered, task completed) re-arms the key for the next occurrence.
/// </summary>
public record NotificationOptions(
    string? DedupeKey = null, bool RequiresAcknowledgement = false, DateTime? ExpiresAt = null);

public record NotificationPreferenceDto(NotificationCategory Category, bool Enabled);

/// <summary>
/// Central mapping from machine type codes to category/severity. Producers only pass the
/// type; routing metadata stays in one place (also the future channel-routing input).
/// </summary>
public static class NotificationTypeCatalog
{
    private static readonly IReadOnlyDictionary<string, (NotificationCategory Category, NotificationSeverity Severity)> Map =
        new Dictionary<string, (NotificationCategory, NotificationSeverity)>(StringComparer.OrdinalIgnoreCase)
        {
            ["absence_requested"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["absence_decided"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["absence_changes_requested"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["qualification_expiring"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["document_expiring"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["employee_document_expiring"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["hr_birthday"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["hr_seniority"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["hr_employment_end"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["trip_assigned"] = (NotificationCategory.Planning, NotificationSeverity.Info),
            ["trip_changed"] = (NotificationCategory.Planning, NotificationSeverity.Warning),
            ["shift_assigned"] = (NotificationCategory.Planning, NotificationSeverity.Info),
            ["planning_changed"] = (NotificationCategory.Planning, NotificationSeverity.Warning),
            ["exception_reported"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["exception_decided"] = (NotificationCategory.Execution, NotificationSeverity.Info),
            ["pod_completed"] = (NotificationCategory.Execution, NotificationSeverity.Info),
            ["eta_changed"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["customer_notification_failed"] = (NotificationCategory.System, NotificationSeverity.Critical),
            ["package_incident"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["package_departure_override"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["package_completion_override"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["package_returned_depot"] = (NotificationCategory.Execution, NotificationSeverity.Info),
            ["inventory_low_stock"] = (NotificationCategory.System, NotificationSeverity.Warning),

            // Inventory status transitions (inventory-tasks-notifications sprint).
            ["inventory_status_low"] = (NotificationCategory.Inventory, NotificationSeverity.Warning),
            ["inventory_status_critical"] = (NotificationCategory.Inventory, NotificationSeverity.Warning),
            ["inventory_status_out"] = (NotificationCategory.Inventory, NotificationSeverity.Warning),
            ["inventory_status_negative"] = (NotificationCategory.Inventory, NotificationSeverity.Critical),

            // Notification-event catalog (corrections wave 4, phase 6) — keys match
            // Messaging.Services.NotificationEventCatalog.EventKey / MessageKinds 1:1.
            ["order_created"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_submitted_portal"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_accepted"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_rejected"] = (NotificationCategory.Orders, NotificationSeverity.Warning),
            ["order_info_requested"] = (NotificationCategory.Orders, NotificationSeverity.Warning),
            ["order_planned"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_pickup_window"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_delivery_window"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_pickup_completed"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_delivery_completed"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["order_delay_detected"] = (NotificationCategory.Orders, NotificationSeverity.Warning),
            ["order_failed_delivery"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["order_damage_registered"] = (NotificationCategory.Execution, NotificationSeverity.Warning),
            ["order_pod_available"] = (NotificationCategory.Orders, NotificationSeverity.Info),
            ["invoice_draft_ready"] = (NotificationCategory.General, NotificationSeverity.Info),
            ["invoice_sent"] = (NotificationCategory.General, NotificationSeverity.Info),
            ["invoice_peppol_queued"] = (NotificationCategory.General, NotificationSeverity.Info),
            ["invoice_peppol_delivered"] = (NotificationCategory.General, NotificationSeverity.Info),
            ["invoice_peppol_failed"] = (NotificationCategory.General, NotificationSeverity.Warning),
            ["invoice_credit_note"] = (NotificationCategory.General, NotificationSeverity.Info),
            ["personnel_qualification_expiry"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["personnel_medical_expiry"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["personnel_document_expiry"] = (NotificationCategory.Hr, NotificationSeverity.Warning),
            ["leave_requested"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["leave_decided"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["employee_note_pinned"] = (NotificationCategory.Hr, NotificationSeverity.Info),
            ["fleet_maintenance_due"] = (NotificationCategory.General, NotificationSeverity.Warning),
            ["fleet_inspection_due"] = (NotificationCategory.General, NotificationSeverity.Warning),
            ["fleet_document_expiry"] = (NotificationCategory.General, NotificationSeverity.Warning),
            ["fleet_damage_created"] = (NotificationCategory.General, NotificationSeverity.Warning),
            ["customer_message_received"] = (NotificationCategory.General, NotificationSeverity.Info),
        };

    public static (NotificationCategory Category, NotificationSeverity Severity) Resolve(string type) =>
        Map.TryGetValue(type, out var entry) ? entry : (NotificationCategory.General, NotificationSeverity.Info);
}

public interface INotificationService
{
    /// <summary>Queues a notification; silently skipped when the recipient is null (no linked user)
    /// or has disabled the resolved category (Critical always delivers).</summary>
    Task NotifyAsync(Guid? userId, string type, string title, string message, string? linkPath, CancellationToken cancellationToken, NotificationOptions? options = null);

    /// <summary>Notifies every active user of the tenant that holds the given permission.</summary>
    Task NotifyPermissionHoldersAsync(string permissionCode, string type, string title, string message, string? linkPath, CancellationToken cancellationToken, NotificationOptions? options = null);

    /// <summary>Notifies every active member of one role.</summary>
    Task NotifyRoleAsync(Guid roleId, string type, string title, string message, string? linkPath, CancellationToken cancellationToken, NotificationOptions? options = null);

    /// <summary>Notifies every active user of the tenant.</summary>
    Task NotifyTenantAsync(string type, string title, string message, string? linkPath, CancellationToken cancellationToken, NotificationOptions? options = null);

    /// <summary>
    /// Marks every unresolved notification with this dedupe key (whole tenant, all recipients)
    /// as resolved, re-arming the key. Call sites own the semantics of "the condition cleared".
    /// </summary>
    Task ResolveByDedupeKeyAsync(string dedupeKey, CancellationToken cancellationToken);

    /// <summary>Acknowledges one of the caller's notifications (also marks it read).</summary>
    Task<bool> AcknowledgeAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDto>> ListMineAsync(NotificationQuery query, CancellationToken cancellationToken);

    Task<int> UnreadCountAsync(CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(Guid id, CancellationToken cancellationToken);

    Task MarkAllReadAsync(CancellationToken cancellationToken);

    /// <summary>Archives (and implicitly reads) one notification.</summary>
    Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>All categories with the current user's enabled flag (default true).</summary>
    Task<IReadOnlyList<NotificationPreferenceDto>> GetMyPreferencesAsync(CancellationToken cancellationToken);

    Task SetMyPreferenceAsync(NotificationCategory category, bool enabled, CancellationToken cancellationToken);
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
        Guid? userId, string type, string title, string message, string? linkPath, CancellationToken cancellationToken,
        NotificationOptions? options = null)
    {
        if (userId is not { } recipient)
        {
            return;
        }

        if (await AddIfEnabledAsync(recipient, type, title, message, linkPath, options, cancellationToken))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task NotifyPermissionHoldersAsync(
        string permissionCode, string type, string title, string message, string? linkPath, CancellationToken cancellationToken,
        NotificationOptions? options = null)
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

        await NotifyManyAsync(recipients, type, title, message, linkPath, options, cancellationToken);
    }

    public async Task NotifyRoleAsync(
        Guid roleId, string type, string title, string message, string? linkPath, CancellationToken cancellationToken,
        NotificationOptions? options = null)
    {
        var tenantId = _tenantContext.TenantId;
        var recipients = await (from ur in _dbContext.UserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId)
                                join u in _dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive)
                                    on ur.UserId equals u.Id
                                join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive)
                                    on ur.RoleId equals r.Id
                                select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        await NotifyManyAsync(recipients, type, title, message, linkPath, options, cancellationToken);
    }

    public async Task NotifyTenantAsync(
        string type, string title, string message, string? linkPath, CancellationToken cancellationToken,
        NotificationOptions? options = null)
    {
        var recipients = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        await NotifyManyAsync(recipients, type, title, message, linkPath, options, cancellationToken);
    }

    public async Task ResolveByDedupeKeyAsync(string dedupeKey, CancellationToken cancellationToken)
    {
        var open = await _dbContext.Notifications
            .Where(n => n.TenantId == _tenantContext.TenantId && n.DedupeKey == dedupeKey && n.ResolvedAt == null)
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var notification in open)
        {
            notification.ResolvedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> AcknowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await Mine().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        notification.AcknowledgedAt ??= now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task NotifyManyAsync(
        IReadOnlyList<Guid> recipients, string type, string title, string message, string? linkPath,
        NotificationOptions? options, CancellationToken cancellationToken)
    {
        var added = false;
        foreach (var recipient in recipients)
        {
            added |= await AddIfEnabledAsync(recipient, type, title, message, linkPath, options, cancellationToken);
        }

        if (added)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Adds the row unless the recipient disabled the category (Critical always goes through)
    /// or an unresolved notification with the same dedupe key already exists for them.</summary>
    private async Task<bool> AddIfEnabledAsync(
        Guid recipient, string type, string title, string message, string? linkPath,
        NotificationOptions? options, CancellationToken cancellationToken)
    {
        var (category, severity) = NotificationTypeCatalog.Resolve(type);

        if (severity != NotificationSeverity.Critical)
        {
            var disabled = await _dbContext.Set<NotificationPreference>().AsNoTracking()
                .AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.UserId == recipient
                               && p.Category == category && !p.Enabled, cancellationToken);
            if (disabled)
            {
                return false;
            }
        }

        if (options?.DedupeKey is { } dedupeKey)
        {
            // Pending (unsaved) rows of the same fan-out batch count too, so two recipients in
            // one batch each get exactly one row and a re-publish within the batch is a no-op.
            var pendingDuplicate = _dbContext.ChangeTracker.Entries<Notification>().Any(e =>
                e.State == EntityState.Added && e.Entity.UserId == recipient && e.Entity.DedupeKey == dedupeKey);
            var storedDuplicate = !pendingDuplicate && await _dbContext.Notifications.AsNoTracking()
                .AnyAsync(n => n.TenantId == _tenantContext.TenantId && n.UserId == recipient
                               && n.DedupeKey == dedupeKey && n.ResolvedAt == null, cancellationToken);
            if (pendingDuplicate || storedDuplicate)
            {
                return false;
            }
        }

        _dbContext.Add(new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            UserId = recipient,
            Type = type,
            Category = category,
            Severity = severity,
            Title = title,
            Message = message,
            LinkPath = linkPath,
            DedupeKey = options?.DedupeKey,
            RequiresAcknowledgement = options?.RequiresAcknowledgement ?? false,
            ExpiresAt = options?.ExpiresAt,
        });
        return true;
    }

    private IQueryable<Notification> Mine()
    {
        var userId = _currentUserContext.CurrentUserId ?? Guid.Empty;
        return _dbContext.Notifications
            .Where(n => n.TenantId == _tenantContext.TenantId && n.UserId == userId);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListMineAsync(
        NotificationQuery query, CancellationToken cancellationToken)
    {
        var rows = Mine().AsNoTracking();
        if (query.UnreadOnly)
        {
            rows = rows.Where(n => !n.IsRead);
        }

        if (!query.IncludeArchived)
        {
            rows = rows.Where(n => !n.IsArchived);
        }

        if (query.Category is { } category)
        {
            rows = rows.Where(n => n.Category == category);
        }

        // Expired notifications behave like archived ones: hidden unless explicitly requested.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!query.IncludeArchived)
        {
            rows = rows.Where(n => n.ExpiresAt == null || n.ExpiresAt > now);
        }

        return await rows
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(query.Take, 1, 100))
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Category, n.Severity, n.Title, n.Message, n.LinkPath,
                n.IsRead, n.IsArchived, n.CreatedAt,
                n.RequiresAcknowledgement, n.AcknowledgedAt, n.ResolvedAt, n.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return Mine().AsNoTracking()
            .CountAsync(n => !n.IsRead && !n.IsArchived && (n.ExpiresAt == null || n.ExpiresAt > now), cancellationToken);
    }

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

    public async Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await Mine().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        if (!notification.IsArchived)
        {
            notification.IsArchived = true;
            notification.ArchivedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetMyPreferencesAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserContext.CurrentUserId ?? Guid.Empty;
        var stored = await _dbContext.Set<NotificationPreference>().AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId && p.UserId == userId)
            .ToDictionaryAsync(p => p.Category, p => p.Enabled, cancellationToken);

        return Enum.GetValues<NotificationCategory>()
            .Select(category => new NotificationPreferenceDto(category, stored.GetValueOrDefault(category, true)))
            .ToList();
    }

    public async Task SetMyPreferenceAsync(
        NotificationCategory category, bool enabled, CancellationToken cancellationToken)
    {
        var userId = _currentUserContext.CurrentUserId ?? Guid.Empty;
        var preference = await _dbContext.Set<NotificationPreference>()
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.UserId == userId
                                      && p.Category == category, cancellationToken);
        if (preference is null)
        {
            _dbContext.Add(new NotificationPreference
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Category = category,
                Enabled = enabled,
            });
        }
        else
        {
            preference.Enabled = enabled;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
