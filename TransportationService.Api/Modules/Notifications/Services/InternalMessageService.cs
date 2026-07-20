using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Notifications.Services;

public record SendInternalMessageRequest(
    string Subject,
    string Body,
    IReadOnlyList<Guid>? UserIds = null,
    Guid? RoleId = null,
    string? RelatedEntityType = null,
    string? RelatedEntityId = null);

public record InternalMessageDto(
    Guid Id,
    string Subject,
    string Body,
    string SenderName,
    DateTime SentAt,
    DateTime? ReadAt,
    string? RelatedEntityType,
    string? RelatedEntityId,
    int RecipientCount);

public record MessageRecipientOptionDto(Guid UserId, string Name);

public interface IInternalMessageService
{
    /// <summary>Sends to explicit users and/or every active member of a role; recipients are deduped.</summary>
    Task<int> SendAsync(SendInternalMessageRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<InternalMessageDto>> ListInboxAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<InternalMessageDto>> ListSentAsync(CancellationToken cancellationToken);

    Task<int> UnreadCountAsync(CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Active users a sender may address (id + display name).</summary>
    Task<IReadOnlyList<MessageRecipientOptionDto>> ListRecipientOptionsAsync(CancellationToken cancellationToken);
}

public class InternalMessageService : IInternalMessageService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public InternalMessageService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        INotificationService notificationService,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _notificationService = notificationService;
        _auditService = auditService;
        _timeProvider = timeProvider;
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
            var members = await _dbContext.UserRoles.AsNoTracking()
                .Where(ur => ur.RoleId == roleId)
                .Join(_dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive),
                    ur => ur.UserId, u => u.Id, (ur, u) => u.Id)
                .ToListAsync(cancellationToken);
            recipientIds.UnionWith(members);
        }

        recipientIds.Remove(senderId);
        if (recipientIds.Count == 0)
        {
            throw new DomainValidationException("Kies minstens één ontvanger.");
        }

        var message = new InternalMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SenderUserId = senderId,
            Subject = request.Subject.Trim(),
            Body = request.Body.Trim(),
            RelatedEntityType = string.IsNullOrWhiteSpace(request.RelatedEntityType) ? null : request.RelatedEntityType.Trim(),
            RelatedEntityId = string.IsNullOrWhiteSpace(request.RelatedEntityId) ? null : request.RelatedEntityId.Trim(),
        };
        _dbContext.Add(message);
        foreach (var userId in recipientIds)
        {
            _dbContext.Add(new InternalMessageRecipient
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                MessageId = message.Id,
                UserId = userId,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Recipients get a system notification pointing at the inbox.
        foreach (var userId in recipientIds)
        {
            await _notificationService.NotifyAsync(userId, "internal_message",
                $"Nieuw bericht: {message.Subject}", "Je hebt een nieuw intern bericht ontvangen.",
                "/inbox", cancellationToken);
        }

        await _auditService.RecordAsync("InternalMessage", message.Id.ToString(), "Sent", null,
            new { message.Subject, RecipientCount = recipientIds.Count }, cancellationToken);

        return recipientIds.Count;
    }

    public async Task<IReadOnlyList<InternalMessageDto>> ListInboxAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return [];
        }

        var tenantId = _tenantContext.TenantId;
        // Anonymous projection first: joining into a positional record ctor does not translate.
        var rows = await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.UserId == userId)
            .Join(_dbContext.Set<InternalMessage>().AsNoTracking(), r => r.MessageId, m => m.Id,
                (r, m) => new { r.ReadAt, m.Id, m.Subject, m.Body, m.SenderUserId, m.CreatedAt, m.RelatedEntityType, m.RelatedEntityId })
            .Join(_dbContext.Users.AsNoTracking(), x => x.SenderUserId, u => u.Id,
                (x, u) => new
                {
                    x.Id, x.Subject, x.Body, SenderName = u.FirstName + " " + u.LastName,
                    x.CreatedAt, x.ReadAt, x.RelatedEntityType, x.RelatedEntityId,
                })
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new InternalMessageDto(
                x.Id, x.Subject, x.Body, x.SenderName, x.CreatedAt, x.ReadAt,
                x.RelatedEntityType, x.RelatedEntityId, 0))
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
                m.RelatedEntityType, m.RelatedEntityId, counts.GetValueOrDefault(m.Id)))
            .ToList();
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return 0;
        }

        return await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
            .CountAsync(r => r.TenantId == _tenantContext.TenantId && r.UserId == userId && r.ReadAt == null,
                cancellationToken);
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
