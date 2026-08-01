using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Notifications.Services;

public record SendInternalMessageRequest(
    string Subject,
    string Body,
    IReadOnlyList<Guid>? UserIds = null,
    Guid? RoleId = null,
    string? RelatedEntityType = null,
    string? RelatedEntityId = null,
    Guid? DepartmentId = null,
    bool AllEmployees = false,
    MessagePriority Priority = MessagePriority.Normal,
    bool RequiresAcknowledgement = false,
    DateTime? VisibleFrom = null,
    DateTime? ExpiresAt = null,
    bool SendEmail = false);

public record InternalMessageDto(
    Guid Id,
    string Subject,
    string Body,
    string SenderName,
    DateTime SentAt,
    DateTime? ReadAt,
    string? RelatedEntityType,
    string? RelatedEntityId,
    int RecipientCount,
    MessagePriority Priority = MessagePriority.Normal,
    bool RequiresAcknowledgement = false,
    DateTime? AcknowledgedAt = null,
    DateTime? VisibleFrom = null,
    DateTime? ExpiresAt = null,
    DateTime? CancelledAt = null);

public record MessageRecipientOptionDto(Guid UserId, string Name);

public record MessageDeliveryRecipientDto(
    Guid UserId, string Name, DateTime? ReadAt, DateTime? AcknowledgedAt,
    string EmailStatus, string? EmailFailureReason);

public record MessageDeliveryStatusDto(
    Guid MessageId, string Subject, DateTime SentAt, DateTime? CancelledAt,
    bool RequiresAcknowledgement, IReadOnlyList<MessageDeliveryRecipientDto> Recipients);

public interface IInternalMessageService
{
    /// <summary>
    /// Sends to explicit users, a role, a department or every employee (deduped).
    /// Role/department/all targeting is bulk and requires messages.send_bulk on top of
    /// messages.send (service-side check: the controller cannot see the targeting shape).
    /// </summary>
    Task<int> SendAsync(SendInternalMessageRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<InternalMessageDto>> ListInboxAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<InternalMessageDto>> ListSentAsync(CancellationToken cancellationToken);

    Task<int> UnreadCountAsync(CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Explicit confirmation by the recipient (only for RequiresAcknowledgement messages).</summary>
    Task<bool> AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Per-recipient delivery state; own messages, or any with messages.view_delivery_status.</summary>
    Task<MessageDeliveryStatusDto?> GetDeliveryStatusAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Withdraws a message; own messages, or any with messages.cancel.</summary>
    Task<bool> CancelAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Active users a sender may address (id + display name).</summary>
    Task<IReadOnlyList<MessageRecipientOptionDto>> ListRecipientOptionsAsync(CancellationToken cancellationToken);
}

public class InternalMessageService : IInternalMessageService
{
    private const string EntityType = "InternalMessage";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly IMessageOutboxService? _messageOutbox;

    public InternalMessageService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        INotificationService notificationService,
        IAuditService auditService,
        TimeProvider timeProvider,
        IPermissionAuthorizationService permissionService,
        IMessageOutboxService? messageOutbox = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _notificationService = notificationService;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _permissionService = permissionService;
        _messageOutbox = messageOutbox;
    }

    public async Task<int> SendAsync(SendInternalMessageRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } senderId)
        {
            throw new DomainValidationException("Geen aangemelde gebruiker.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new DomainValidationException("subject", "Een onderwerp is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new DomainValidationException("body", "Een bericht is verplicht.");
        }

        if (request.VisibleFrom is { } visibleFrom && request.ExpiresAt is { } expiresAt && expiresAt <= visibleFrom)
        {
            throw new DomainValidationException("expiresAt", "De vervaldatum moet na 'zichtbaar vanaf' liggen.");
        }

        var isBulk = request.RoleId is not null || request.DepartmentId is not null || request.AllEmployees;
        if (isBulk)
        {
            // Bulk fan-out is deliberately a separate permission (messages.send_bulk); the
            // controller only checks messages.send because it cannot see the targeting shape.
            var mayBulk = await _permissionService.UserHasPermissionAsync(
                senderId, PermissionCodes.MessagesSendBulk, cancellationToken);
            if (!mayBulk)
            {
                throw new DomainValidationException("Bulkberichten (rol, afdeling of iedereen) vereisen een extra machtiging.");
            }
        }

        var tenantId = _tenantContext.TenantId;
        var recipientIds = new HashSet<Guid>();

        if (request.UserIds is { Count: > 0 })
        {
            var known = await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.IsActive && request.UserIds.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
            if (known.Count != request.UserIds.Distinct().Count())
            {
                throw new DomainValidationException("Eén of meer gekozen ontvangers bestaan niet.");
            }

            recipientIds.UnionWith(known);
        }

        if (request.RoleId is { } roleId)
        {
            var roleInTenant = await _dbContext.Roles.AsNoTracking()
                .AnyAsync(r => r.TenantId == tenantId && r.Id == roleId, cancellationToken);
            if (!roleInTenant)
            {
                throw new DomainValidationException("roleId", "De rol bestaat niet.");
            }

            var members = await _dbContext.UserRoles.AsNoTracking()
                .Where(ur => ur.RoleId == roleId)
                .Join(_dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive),
                    ur => ur.UserId, u => u.Id, (ur, u) => u.Id)
                .ToListAsync(cancellationToken);
            recipientIds.UnionWith(members);
        }

        if (request.DepartmentId is { } departmentId)
        {
            var departmentInTenant = await _dbContext.Departments.AsNoTracking()
                .AnyAsync(d => d.TenantId == tenantId && d.Id == departmentId, cancellationToken);
            if (!departmentInTenant)
            {
                throw new DomainValidationException("departmentId", "De afdeling bestaat niet.");
            }

            var members = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.IsActive && e.DepartmentId == departmentId)
                .Join(_dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive),
                    e => e.Id, u => u.EmployeeId, (e, u) => u.Id)
                .ToListAsync(cancellationToken);
            recipientIds.UnionWith(members);
        }

        if (request.AllEmployees)
        {
            var everyone = await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.IsActive && u.EmployeeId != null)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
            recipientIds.UnionWith(everyone);
        }

        recipientIds.Remove(senderId);
        if (recipientIds.Count == 0)
        {
            throw new DomainValidationException("Kies minstens één ontvanger.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var visibleNow = request.VisibleFrom is not { } from || from <= now;
        var message = new InternalMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SenderUserId = senderId,
            Subject = request.Subject.Trim(),
            Body = request.Body.Trim(),
            RelatedEntityType = string.IsNullOrWhiteSpace(request.RelatedEntityType) ? null : request.RelatedEntityType.Trim(),
            RelatedEntityId = string.IsNullOrWhiteSpace(request.RelatedEntityId) ? null : request.RelatedEntityId.Trim(),
            Priority = request.Priority,
            RequiresAcknowledgement = request.RequiresAcknowledgement,
            VisibleFrom = request.VisibleFrom,
            ExpiresAt = request.ExpiresAt,
            EmailRequested = request.SendEmail,
            NotifiedAt = visibleNow ? now : null,
        };
        _dbContext.Add(message);
        var recipients = new List<InternalMessageRecipient>();
        foreach (var userId in recipientIds)
        {
            var recipient = new InternalMessageRecipient
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                MessageId = message.Id,
                UserId = userId,
            };
            recipients.Add(recipient);
            _dbContext.Add(recipient);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // In-app announcement only when the message is already visible; the sweep announces
        // future-visible messages when their window opens (NotifiedAt guards doubles).
        if (visibleNow)
        {
            foreach (var userId in recipientIds)
            {
                await _notificationService.NotifyAsync(userId, "internal_message",
                    $"Nieuw bericht: {message.Subject}",
                    message.RequiresAcknowledgement
                        ? "Je hebt een nieuw intern bericht dat bevestiging vereist."
                        : "Je hebt een nieuw intern bericht ontvangen.",
                    "/inbox", cancellationToken);
            }
        }

        if (request.SendEmail && _messageOutbox is not null)
        {
            await QueueEmailCopiesAsync(message, recipients, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, message.Id.ToString(), "Sent", null,
            new
            {
                message.Subject, RecipientCount = recipientIds.Count, message.Priority,
                message.RequiresAcknowledgement, Bulk = isBulk, Email = request.SendEmail,
            },
            cancellationToken);

        return recipientIds.Count;
    }

    /// <summary>
    /// One outbox row per recipient with a deterministic idempotency key, so a retried send
    /// never duplicates mail. The rendered body carries only a preview — the message itself
    /// stays in-app.
    /// </summary>
    private async Task QueueEmailCopiesAsync(
        InternalMessage message, IReadOnlyList<InternalMessageRecipient> recipients, CancellationToken cancellationToken)
    {
        var userIds = recipients.Select(r => r.UserId).ToList();
        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.EmployeeId })
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var senderName = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == message.SenderUserId)
            .Select(u => u.FirstName + " " + u.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? "een collega";
        var preview = message.Body.Length <= 300 ? message.Body : message.Body[..300] + "…";

        var linked = false;
        foreach (var recipient in recipients)
        {
            if (!users.TryGetValue(recipient.UserId, out var user))
            {
                continue;
            }

            var result = await _messageOutbox!.QueueAsync(new MessageRequest(
                MessageKinds.EmployeeMessageReceived,
                MessageOwnerType.Employee,
                user.EmployeeId ?? user.Id,
                new Dictionary<string, string>
                {
                    ["subject"] = message.Subject,
                    ["employeeName"] = $"{user.FirstName} {user.LastName}",
                    ["senderName"] = senderName,
                    ["preview"] = preview,
                },
                EntityType,
                message.Id.ToString(),
                IdempotencyKey: $"employee_message:{message.Id}:{recipient.UserId}",
                OverrideAddress: user.Email,
                OverrideName: $"{user.FirstName} {user.LastName}"), cancellationToken);
            if (result.MessageId is { } outboxId)
            {
                recipient.EmailOutboxMessageId = outboxId;
                linked = true;
            }
        }

        if (linked)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<InternalMessageDto>> ListInboxAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return [];
        }

        var tenantId = _tenantContext.TenantId;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // Anonymous projection first: joining into a positional record ctor does not translate.
        var rows = await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.UserId == userId)
            .Join(_dbContext.Set<InternalMessage>().AsNoTracking()
                    .Where(m => m.CancelledAt == null
                                && (m.VisibleFrom == null || m.VisibleFrom <= now)
                                && (m.ExpiresAt == null || m.ExpiresAt > now)),
                r => r.MessageId, m => m.Id,
                (r, m) => new
                {
                    r.ReadAt, r.AcknowledgedAt, m.Id, m.Subject, m.Body, m.SenderUserId, m.CreatedAt,
                    m.RelatedEntityType, m.RelatedEntityId, m.Priority, m.RequiresAcknowledgement,
                    m.VisibleFrom, m.ExpiresAt,
                })
            .Join(_dbContext.Users.AsNoTracking(), x => x.SenderUserId, u => u.Id,
                (x, u) => new
                {
                    x.Id, x.Subject, x.Body, SenderName = u.FirstName + " " + u.LastName,
                    x.CreatedAt, x.ReadAt, x.AcknowledgedAt, x.RelatedEntityType, x.RelatedEntityId,
                    x.Priority, x.RequiresAcknowledgement, x.VisibleFrom, x.ExpiresAt,
                })
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new InternalMessageDto(
                x.Id, x.Subject, x.Body, x.SenderName, x.CreatedAt, x.ReadAt,
                x.RelatedEntityType, x.RelatedEntityId, 0,
                x.Priority, x.RequiresAcknowledgement, x.AcknowledgedAt, x.VisibleFrom, x.ExpiresAt))
            .ToList();
    }

    public async Task<IReadOnlyList<InternalMessageDto>> ListSentAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return [];
        }

        var tenantId = _tenantContext.TenantId;
        var messages = await _dbContext.Set<InternalMessage>().AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.SenderUserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var messageIds = messages.Select(m => m.Id).ToList();
        var counts = await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
            .Where(r => messageIds.Contains(r.MessageId))
            .GroupBy(r => r.MessageId)
            .Select(g => new { MessageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MessageId, g => g.Count, cancellationToken);

        return messages
            .Select(m => new InternalMessageDto(
                m.Id, m.Subject, m.Body, "Ik", m.CreatedAt, null,
                m.RelatedEntityType, m.RelatedEntityId, counts.GetValueOrDefault(m.Id),
                m.Priority, m.RequiresAcknowledgement, null, m.VisibleFrom, m.ExpiresAt, m.CancelledAt))
            .ToList();
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.UserId == userId && r.ReadAt == null)
            .Join(_dbContext.Set<InternalMessage>().AsNoTracking()
                    .Where(m => m.CancelledAt == null
                                && (m.VisibleFrom == null || m.VisibleFrom <= now)
                                && (m.ExpiresAt == null || m.ExpiresAt > now)),
                r => r.MessageId, m => m.Id, (r, m) => r.Id)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> MarkReadAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        var recipient = await _dbContext.Set<InternalMessageRecipient>()
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.MessageId == messageId && r.UserId == userId,
                cancellationToken);
        if (recipient is null)
        {
            return false;
        }

        if (recipient.ReadAt is null)
        {
            recipient.ReadAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        var recipient = await _dbContext.Set<InternalMessageRecipient>()
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.MessageId == messageId && r.UserId == userId,
                cancellationToken);
        if (recipient is null)
        {
            return false;
        }

        var requiresAcknowledgement = await _dbContext.Set<InternalMessage>().AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => m.RequiresAcknowledgement)
            .FirstOrDefaultAsync(cancellationToken);
        if (!requiresAcknowledgement)
        {
            throw new DomainValidationException("Dit bericht vraagt geen bevestiging.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        recipient.ReadAt ??= now;
        if (recipient.AcknowledgedAt is null)
        {
            recipient.AcknowledgedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, messageId.ToString(), "Acknowledged",
                null, new { RecipientUserId = userId }, cancellationToken);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<MessageDeliveryStatusDto?> GetDeliveryStatusAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        var message = await _dbContext.Set<InternalMessage>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == _tenantContext.TenantId && m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return null;
        }

        if (message.SenderUserId != userId
            && !await _permissionService.UserHasPermissionAsync(
                userId, PermissionCodes.MessagesViewDeliveryStatus, cancellationToken))
        {
            return null; // 404: don't reveal that someone else's message exists
        }

        var recipients = await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.MessageId == messageId)
            .Join(_dbContext.Users.AsNoTracking(), r => r.UserId, u => u.Id,
                (r, u) => new { r.UserId, Name = u.FirstName + " " + u.LastName, r.ReadAt, r.AcknowledgedAt, r.EmailOutboxMessageId })
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var outboxIds = recipients.Where(r => r.EmailOutboxMessageId.HasValue).Select(r => r.EmailOutboxMessageId!.Value).ToList();
        var outboxStates = await _dbContext.OutboxMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId && outboxIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Status, m.FailureReason })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var rows = recipients
            .Select(r =>
            {
                var email = "Geen";
                string? failure = null;
                if (r.EmailOutboxMessageId is { } outboxId && outboxStates.TryGetValue(outboxId, out var state))
                {
                    email = state.Status.ToString();
                    failure = state.FailureReason;
                }

                return new MessageDeliveryRecipientDto(r.UserId, r.Name, r.ReadAt, r.AcknowledgedAt, email, failure);
            })
            .ToList();

        return new MessageDeliveryStatusDto(
            message.Id, message.Subject, message.CreatedAt, message.CancelledAt,
            message.RequiresAcknowledgement, rows);
    }

    public async Task<bool> CancelAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        var message = await _dbContext.Set<InternalMessage>()
            .FirstOrDefaultAsync(m => m.TenantId == _tenantContext.TenantId && m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return false;
        }

        if (message.SenderUserId != userId
            && !await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.MessagesCancel, cancellationToken))
        {
            return false;
        }

        if (message.CancelledAt is null)
        {
            message.CancelledAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, messageId.ToString(), "Cancelled",
                new { message.Subject }, null, cancellationToken);
        }

        return true;
    }

    public async Task<IReadOnlyList<MessageRecipientOptionDto>> ListRecipientOptionsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new MessageRecipientOptionDto(u.Id, u.FirstName + " " + u.LastName))
            .ToListAsync(cancellationToken);
    }
}
