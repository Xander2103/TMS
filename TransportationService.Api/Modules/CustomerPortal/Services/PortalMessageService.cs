using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public record SendPortalMessageRequest(
    string TitleNl, string BodyNl,
    string? TitleFr = null, string? BodyFr = null,
    string? TitleEn = null, string? BodyEn = null,
    IReadOnlyList<Guid>? CustomerIds = null,
    IReadOnlyList<Guid>? PortalUserIds = null,
    MessagePriority Priority = MessagePriority.Normal,
    PortalMessageDisplayMode DisplayMode = PortalMessageDisplayMode.Notification,
    bool RequiresAcknowledgement = false,
    DateTime? VisibleFrom = null,
    DateTime? ExpiresAt = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    bool SendEmail = false);

public record PortalMessageAdminDto(
    Guid Id, string TitleNl, string? TitleFr, string? TitleEn,
    string BodyNl, string? BodyFr, string? BodyEn,
    MessagePriority Priority, PortalMessageDisplayMode DisplayMode, bool RequiresAcknowledgement,
    DateTime? VisibleFrom, DateTime? ExpiresAt, string? RelatedEntityType, Guid? RelatedEntityId,
    bool EmailRequested, DateTime? CancelledAt, DateTime CreatedAt,
    IReadOnlyList<string> CustomerNames);

public record PortalMessageDeliveryRowDto(
    Guid UserId, string Name, string CustomerName, DateTime? ReadAt, DateTime? AcknowledgedAt,
    string EmailStatus, string? EmailFailureReason);

public record PortalMessageDeliveryStatusDto(
    Guid MessageId, string TitleNl, DateTime CreatedAt, DateTime? CancelledAt,
    bool RequiresAcknowledgement, IReadOnlyList<PortalMessageDeliveryRowDto> Recipients);

/// <summary>Feed item with content resolved to the caller's language.</summary>
public record PortalMessageFeedItemDto(
    Guid Id, string Title, string Body, string Language,
    MessagePriority Priority, PortalMessageDisplayMode DisplayMode, bool RequiresAcknowledgement,
    string? RelatedEntityType, Guid? RelatedEntityId,
    DateTime PublishedAt, DateTime? ExpiresAt, DateTime? ReadAt, DateTime? AcknowledgedAt);

public interface IPortalMessageService
{
    // Internal (staff) side.
    Task<PortalMessageAdminDto> SendAsync(SendPortalMessageRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PortalMessageAdminDto>> ListAsync(CancellationToken cancellationToken);
    Task<PortalMessageDeliveryStatusDto?> GetDeliveryStatusAsync(Guid messageId, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid messageId, CancellationToken cancellationToken);

    // Portal side (scoped to the caller's own customer link).
    Task<IReadOnlyList<PortalMessageFeedItemDto>?> ListFeedAsync(CancellationToken cancellationToken);
    Task<int?> FeedUnreadCountAsync(CancellationToken cancellationToken);
    Task<bool> MarkFeedReadAsync(Guid messageId, CancellationToken cancellationToken);
    Task<bool> AcknowledgeFeedAsync(Guid messageId, CancellationToken cancellationToken);
}

public class PortalMessageService : IPortalMessageService
{
    private const string EntityType = "PortalMessage";
    private static readonly string[] AllowedRelatedTypes = ["order", "invoice"];
    // Centrale catalogus — geen eigen kopie van de taallijst meer.
    private static readonly IReadOnlyList<string> SupportedLanguages = Common.SupportedLanguages.All;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly IMessageOutboxService? _messageOutbox;

    public PortalMessageService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext,
        IAuditService auditService, TimeProvider timeProvider,
        IPermissionAuthorizationService permissionService,
        IMessageOutboxService? messageOutbox = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _permissionService = permissionService;
        _messageOutbox = messageOutbox;
    }

    // ---------------------------------------------------------------- staff side

    public async Task<PortalMessageAdminDto> SendAsync(SendPortalMessageRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } senderId)
        {
            throw new DomainValidationException("Geen aangemelde gebruiker.");
        }

        if (string.IsNullOrWhiteSpace(request.TitleNl))
        {
            throw new DomainValidationException("titleNl", "De Nederlandse titel is verplicht (basistaal).");
        }

        if (string.IsNullOrWhiteSpace(request.BodyNl))
        {
            throw new DomainValidationException("bodyNl", "De Nederlandse inhoud is verplicht (basistaal).");
        }

        if (request.VisibleFrom is { } visibleFrom && request.ExpiresAt is { } expiresAt && expiresAt <= visibleFrom)
        {
            throw new DomainValidationException("expiresAt", "De vervaldatum moet na 'zichtbaar vanaf' liggen.");
        }

        var tenantId = _tenantContext.TenantId;

        // Resolve targeting: explicit portal users (each pins its customer) and/or whole customers.
        var recipients = new List<PortalMessageRecipient>();
        var customerIds = new HashSet<Guid>();

        if (request.PortalUserIds is { Count: > 0 })
        {
            var users = await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.IsActive && u.CustomerId != null
                            && request.PortalUserIds.Contains(u.Id))
                .Select(u => new { u.Id, CustomerId = u.CustomerId!.Value })
                .ToListAsync(cancellationToken);
            if (users.Count != request.PortalUserIds.Distinct().Count())
            {
                throw new DomainValidationException("portalUserIds", "Eén of meer portaalgebruikers bestaan niet.");
            }

            foreach (var user in users)
            {
                customerIds.Add(user.CustomerId);
                recipients.Add(new PortalMessageRecipient
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = user.CustomerId, UserId = user.Id,
                });
            }
        }

        if (request.CustomerIds is { Count: > 0 })
        {
            var known = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && request.CustomerIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (known.Count != request.CustomerIds.Distinct().Count())
            {
                throw new DomainValidationException("customerIds", "Eén of meer klanten bestaan niet.");
            }

            foreach (var customerId in known)
            {
                customerIds.Add(customerId);
                recipients.Add(new PortalMessageRecipient
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, UserId = null,
                });
            }
        }

        if (recipients.Count == 0)
        {
            throw new DomainValidationException("Kies minstens één klant of portaalgebruiker.");
        }

        // Multi-customer fan-out is bulk (service-side gate: the controller cannot see this).
        if (customerIds.Count > 1
            && !await _permissionService.UserHasPermissionAsync(senderId, PermissionCodes.PortalMessagesSendBulk, cancellationToken))
        {
            throw new DomainValidationException("Berichten naar meerdere klanten vereisen een extra machtiging.");
        }

        await ValidateRelatedEntityAsync(request, customerIds, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var message = new PortalMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TitleNl = request.TitleNl.Trim(),
            TitleFr = Trim(request.TitleFr),
            TitleEn = Trim(request.TitleEn),
            BodyNl = request.BodyNl.Trim(),
            BodyFr = Trim(request.BodyFr),
            BodyEn = Trim(request.BodyEn),
            Priority = request.Priority,
            DisplayMode = request.DisplayMode,
            RequiresAcknowledgement = request.DisplayMode == PortalMessageDisplayMode.BlockingAcknowledgement
                || request.RequiresAcknowledgement,
            VisibleFrom = request.VisibleFrom,
            ExpiresAt = request.ExpiresAt,
            RelatedEntityType = Trim(request.RelatedEntityType)?.ToLowerInvariant(),
            RelatedEntityId = request.RelatedEntityId,
            EmailRequested = request.SendEmail,
        };
        _dbContext.Add(message);
        foreach (var recipient in recipients)
        {
            recipient.PortalMessageId = message.Id;
            _dbContext.Add(recipient);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var emailed = 0;
        if (request.SendEmail && _messageOutbox is not null
            && (request.VisibleFrom is not { } from || from <= now))
        {
            emailed = await QueueEmailsAsync(message, recipients, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, message.Id.ToString(), "Sent", null,
            new
            {
                message.TitleNl, Languages = DescribeLanguages(message), Customers = customerIds.Count,
                Recipients = recipients.Count, message.DisplayMode, message.RequiresAcknowledgement,
                message.VisibleFrom, Email = request.SendEmail, Emailed = emailed,
            },
            cancellationToken);

        return await MapAdminAsync(message, cancellationToken);
    }

    private async Task ValidateRelatedEntityAsync(
        SendPortalMessageRequest request, IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RelatedEntityType) && request.RelatedEntityId is null)
        {
            return;
        }

        var type = request.RelatedEntityType?.Trim().ToLowerInvariant();
        if (type is null || request.RelatedEntityId is not { } relatedId || !AllowedRelatedTypes.Contains(type))
        {
            throw new DomainValidationException("relatedEntityType", "Koppeling vereist type (order of invoice) én id.");
        }

        if (customerIds.Count != 1)
        {
            throw new DomainValidationException("relatedEntityId",
                "Een gekoppeld record kan alleen bij een bericht aan precies één klant.");
        }

        var customerId = customerIds.Single();
        var belongs = type switch
        {
            "order" => await _dbContext.TransportOrders.AsNoTracking()
                .AnyAsync(o => o.TenantId == _tenantContext.TenantId && o.Id == relatedId && o.CustomerId == customerId, cancellationToken),
            _ => await _dbContext.Invoices.AsNoTracking()
                .AnyAsync(i => i.TenantId == _tenantContext.TenantId && i.Id == relatedId && i.CustomerId == customerId, cancellationToken),
        };
        if (!belongs)
        {
            throw new DomainValidationException("relatedEntityId", "Het gekoppelde record bestaat niet voor deze klant.");
        }
    }

    /// <summary>
    /// One e-mail per targeted portal user, in the user's preferred language (fallback
    /// customer default → nl), carrying the translated content of the message itself.
    /// </summary>
    private async Task<int> QueueEmailsAsync(
        PortalMessage message, IReadOnlyList<PortalMessageRecipient> recipients, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var wholeCustomerIds = recipients.Where(r => r.UserId is null).Select(r => r.CustomerId).Distinct().ToList();
        var explicitUserIds = recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList();

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive && u.CustomerId != null
                        && (explicitUserIds.Contains(u.Id) || wholeCustomerIds.Contains(u.CustomerId!.Value)))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, CustomerId = u.CustomerId!.Value, u.PreferredLanguageCode })
            .ToListAsync(cancellationToken);
        var customerDefaults = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && users.Select(u => u.CustomerId).Distinct().Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DefaultLanguageCode, cancellationToken);

        var emailed = 0;
        foreach (var user in users.DistinctBy(u => u.Id))
        {
            var language = ResolveLanguage(user.PreferredLanguageCode, customerDefaults.GetValueOrDefault(user.CustomerId));
            var (title, body) = Localize(message, language);
            var result = await _messageOutbox!.QueueAsync(new MessageRequest(
                MessageKinds.PortalMessagePublished,
                MessageOwnerType.Customer,
                user.CustomerId,
                new Dictionary<string, string>
                {
                    ["title"] = title,
                    ["body"] = body,
                    ["recipientName"] = $"{user.FirstName} {user.LastName}",
                },
                EntityType,
                message.Id.ToString(),
                IdempotencyKey: $"portal_message:{message.Id}:{user.Id}",
                OverrideAddress: user.Email,
                OverrideName: $"{user.FirstName} {user.LastName}",
                OverrideLanguage: language), cancellationToken);
            if (result.Outcome == QueueOutcome.Queued)
            {
                emailed++;
            }
        }

        return emailed;
    }

    public async Task<IReadOnlyList<PortalMessageAdminDto>> ListAsync(CancellationToken cancellationToken)
    {
        var messages = await _dbContext.PortalMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var result = new List<PortalMessageAdminDto>(messages.Count);
        foreach (var message in messages)
        {
            result.Add(await MapAdminAsync(message, cancellationToken));
        }

        return result;
    }

    public async Task<PortalMessageDeliveryStatusDto?> GetDeliveryStatusAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var message = await _dbContext.PortalMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return null;
        }

        var recipients = await _dbContext.PortalMessageRecipients.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.PortalMessageId == messageId)
            .ToListAsync(cancellationToken);
        var wholeCustomerIds = recipients.Where(r => r.UserId is null).Select(r => r.CustomerId).Distinct().ToList();
        var explicitUserIds = recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList();

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.CustomerId != null
                        && (explicitUserIds.Contains(u.Id) || wholeCustomerIds.Contains(u.CustomerId!.Value)))
            .Join(_dbContext.Customers.AsNoTracking(), u => u.CustomerId, c => c.Id,
                (u, c) => new { u.Id, Name = u.FirstName + " " + u.LastName, u.Email, CustomerName = c.Name })
            .OrderBy(u => u.CustomerName).ThenBy(u => u.Name)
            .ToListAsync(cancellationToken);

        var receipts = await _dbContext.PortalMessageReceipts.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.PortalMessageId == messageId)
            .ToDictionaryAsync(r => r.UserId, cancellationToken);
        var outboxByAddress = await _dbContext.OutboxMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.RelatedEntityType == EntityType
                        && m.RelatedEntityId == messageId.ToString())
            .GroupBy(m => m.RecipientAddress)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
            .ToListAsync(cancellationToken);
        var outboxLookup = outboxByAddress.ToDictionary(m => m.RecipientAddress, StringComparer.OrdinalIgnoreCase);

        var rows = users
            .DistinctBy(u => u.Id)
            .Select(u =>
            {
                receipts.TryGetValue(u.Id, out var receipt);
                var email = "Geen";
                string? failure = null;
                if (outboxLookup.TryGetValue(u.Email, out var outbox))
                {
                    email = outbox.Status.ToString();
                    failure = outbox.FailureReason;
                }

                return new PortalMessageDeliveryRowDto(
                    u.Id, u.Name, u.CustomerName, receipt?.ReadAt, receipt?.AcknowledgedAt, email, failure);
            })
            .ToList();

        return new PortalMessageDeliveryStatusDto(
            message.Id, message.TitleNl, message.CreatedAt, message.CancelledAt,
            message.RequiresAcknowledgement, rows);
    }

    public async Task<bool> CancelAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        var message = await _dbContext.PortalMessages
            .FirstOrDefaultAsync(m => m.TenantId == _tenantContext.TenantId && m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return false;
        }

        if (message.CreatedByUserId != userId
            && !await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.PortalMessagesCancel, cancellationToken))
        {
            return false;
        }

        if (message.CancelledAt is null)
        {
            message.CancelledAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, messageId.ToString(), "Cancelled",
                new { message.TitleNl }, null, cancellationToken);
        }

        return true;
    }

    // ---------------------------------------------------------------- portal side

    private async Task<(Guid UserId, Guid CustomerId, string? UserLanguage, string? CustomerLanguage)?> MyPortalContextAsync(
        CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        var link = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.CustomerId != null)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                u => u.CustomerId, c => c.Id,
                (u, c) => new { u.Id, CustomerId = c.Id, u.PreferredLanguageCode, c.DefaultLanguageCode })
            .FirstOrDefaultAsync(cancellationToken);
        return link is null ? null : (link.Id, link.CustomerId, link.PreferredLanguageCode, link.DefaultLanguageCode);
    }

    private IQueryable<PortalMessage> VisibleMessagesFor(Guid customerId, Guid userId, DateTime now) =>
        _dbContext.PortalMessageRecipients.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.CustomerId == customerId
                        && (r.UserId == null || r.UserId == userId))
            .Join(_dbContext.PortalMessages.AsNoTracking()
                    .Where(m => m.CancelledAt == null
                                && (m.VisibleFrom == null || m.VisibleFrom <= now)
                                && (m.ExpiresAt == null || m.ExpiresAt > now)),
                r => r.PortalMessageId, m => m.Id, (r, m) => m)
            .Distinct();

    public async Task<IReadOnlyList<PortalMessageFeedItemDto>?> ListFeedAsync(CancellationToken cancellationToken)
    {
        if (await MyPortalContextAsync(cancellationToken) is not { } context)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var messages = await VisibleMessagesFor(context.CustomerId, context.UserId, now)
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        var messageIds = messages.Select(m => m.Id).ToList();
        var receipts = await _dbContext.PortalMessageReceipts.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.UserId == context.UserId && messageIds.Contains(r.PortalMessageId))
            .ToDictionaryAsync(r => r.PortalMessageId, cancellationToken);

        var language = ResolveLanguage(context.UserLanguage, context.CustomerLanguage);
        return messages
            .Select(m =>
            {
                receipts.TryGetValue(m.Id, out var receipt);
                var (title, body) = Localize(m, language);
                return new PortalMessageFeedItemDto(
                    m.Id, title, body, language, m.Priority, m.DisplayMode, m.RequiresAcknowledgement,
                    m.RelatedEntityType, m.RelatedEntityId,
                    m.VisibleFrom ?? m.CreatedAt, m.ExpiresAt, receipt?.ReadAt, receipt?.AcknowledgedAt);
            })
            .ToList();
    }

    public async Task<int?> FeedUnreadCountAsync(CancellationToken cancellationToken)
    {
        if (await MyPortalContextAsync(cancellationToken) is not { } context)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var readIds = _dbContext.PortalMessageReceipts.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.UserId == context.UserId && r.ReadAt != null)
            .Select(r => r.PortalMessageId);
        return await VisibleMessagesFor(context.CustomerId, context.UserId, now)
            .CountAsync(m => !readIds.Contains(m.Id), cancellationToken);
    }

    public Task<bool> MarkFeedReadAsync(Guid messageId, CancellationToken cancellationToken) =>
        StampReceiptAsync(messageId, acknowledge: false, cancellationToken);

    public Task<bool> AcknowledgeFeedAsync(Guid messageId, CancellationToken cancellationToken) =>
        StampReceiptAsync(messageId, acknowledge: true, cancellationToken);

    private async Task<bool> StampReceiptAsync(Guid messageId, bool acknowledge, CancellationToken cancellationToken)
    {
        if (await MyPortalContextAsync(cancellationToken) is not { } context)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var message = await VisibleMessagesFor(context.CustomerId, context.UserId, now)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return false; // foreign/hidden message: indistinguishable from non-existent (404)
        }

        if (acknowledge && !message.RequiresAcknowledgement)
        {
            throw new DomainValidationException("Dit bericht vraagt geen bevestiging.");
        }

        var receipt = await _dbContext.PortalMessageReceipts
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.PortalMessageId == messageId
                                      && r.UserId == context.UserId, cancellationToken);
        if (receipt is null)
        {
            receipt = new PortalMessageReceipt
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                PortalMessageId = messageId,
                UserId = context.UserId,
            };
            _dbContext.Add(receipt);
        }

        receipt.ReadAt ??= now;
        if (acknowledge && receipt.AcknowledgedAt is null)
        {
            receipt.AcknowledgedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, messageId.ToString(), "AcknowledgedInPortal",
                null, new { PortalUserId = context.UserId }, cancellationToken);
            return true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---------------------------------------------------------------- helpers

    public static string ResolveLanguage(string? userPreference, string? customerDefault)
    {
        var candidate = (userPreference ?? customerDefault ?? "nl").Trim().ToLowerInvariant();
        return SupportedLanguages.Contains(candidate) ? candidate : "nl";
    }

    public static (string Title, string Body) Localize(PortalMessage message, string language) => language switch
    {
        "fr" => (FirstFilled(message.TitleFr, message.TitleNl), FirstFilled(message.BodyFr, message.BodyNl)),
        "en" => (FirstFilled(message.TitleEn, message.TitleNl), FirstFilled(message.BodyEn, message.BodyNl)),
        _ => (message.TitleNl, message.BodyNl),
    };

    private static string FirstFilled(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string DescribeLanguages(PortalMessage message)
    {
        var languages = new List<string> { "nl" };
        if (!string.IsNullOrWhiteSpace(message.TitleFr) || !string.IsNullOrWhiteSpace(message.BodyFr))
        {
            languages.Add("fr");
        }

        if (!string.IsNullOrWhiteSpace(message.TitleEn) || !string.IsNullOrWhiteSpace(message.BodyEn))
        {
            languages.Add("en");
        }

        return string.Join(",", languages);
    }

    private async Task<PortalMessageAdminDto> MapAdminAsync(PortalMessage message, CancellationToken cancellationToken)
    {
        var customerNames = await _dbContext.PortalMessageRecipients.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.PortalMessageId == message.Id)
            .Join(_dbContext.Customers.AsNoTracking(), r => r.CustomerId, c => c.Id, (r, c) => c.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
        return new PortalMessageAdminDto(
            message.Id, message.TitleNl, message.TitleFr, message.TitleEn,
            message.BodyNl, message.BodyFr, message.BodyEn,
            message.Priority, message.DisplayMode, message.RequiresAcknowledgement,
            message.VisibleFrom, message.ExpiresAt, message.RelatedEntityType, message.RelatedEntityId,
            message.EmailRequested, message.CancelledAt, message.CreatedAt, customerNames);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
