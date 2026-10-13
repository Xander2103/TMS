using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>Provider seam per channel. Real providers (SMTP/SendGrid/Twilio/…) plug in here;
/// no vendor behaviour lives outside an implementation of this interface.</summary>
public interface IMessageChannelProvider
{
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>Marker interfaces so DI can distinguish the two channels.</summary>
public interface IEmailProvider : IMessageChannelProvider;

public interface ISmsProvider : IMessageChannelProvider;

/// <summary>
/// Development sink: "delivers" by writing the message to App_Data/message-sink so developers
/// can inspect exactly what would have gone out. No external calls, no credentials.
/// </summary>
public sealed class DevelopmentSinkProvider : IEmailProvider, ISmsProvider
{
    private readonly string _root;

    public DevelopmentSinkProvider(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{message.Channel}-{message.Id:N}.txt".ToLowerInvariant();
        var content =
            $"Channel: {message.Channel}\nKind: {message.Kind}\nTo: {message.RecipientName} <{message.RecipientAddress}>\n"
            + $"Language: {message.Language}\nSubject: {message.Subject}\n\n{message.Body}\n";
        await File.WriteAllTextAsync(Path.Combine(_root, fileName), content, cancellationToken);
    }
}

/// <summary>
/// Owns delivery of pending outbox rows: backoff retries, permanent failure alerting and the
/// one-shot channel fallback. Runs tenant-agnostic (rows carry their tenant).
/// </summary>
public class MessageDispatcher
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(5);

    private readonly TransportationDbContext _dbContext;
    private readonly IMessageChannelProvider _emailProvider;
    private readonly IMessageChannelProvider _smsProvider;
    private readonly TimeProvider _timeProvider;

    public MessageDispatcher(
        TransportationDbContext dbContext,
        IMessageChannelProvider emailProvider,
        IMessageChannelProvider smsProvider,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _emailProvider = emailProvider;
        _smsProvider = smsProvider;
        _timeProvider = timeProvider;
    }

    public async Task<int> DispatchPendingAsync(int max, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var due = await _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(Math.Clamp(max, 1, 100))
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in due)
        {
            message.AttemptCount += 1;
            message.LastAttemptAt = now;
            try
            {
                var provider = message.Channel == MessageChannel.Email ? _emailProvider : _smsProvider;
                await provider.SendAsync(message, cancellationToken);
                message.Status = OutboxStatus.Sent;
                message.SentAt = now;
                message.FailureReason = null;
                ScrubCredentialBody(message);
                sent += 1;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.FailureReason = exception.Message;
                if (message.AttemptCount >= MaxAttempts)
                {
                    message.Status = OutboxStatus.Failed;
                    ScrubCredentialBody(message);
                    await HandlePermanentFailureAsync(message, cancellationToken);
                }
                else
                {
                    // 5, 10, 20, 40 minutes.
                    message.NextAttemptAt = now + BaseBackoff * Math.Pow(2, message.AttemptCount - 1);
                }
            }
        }

        if (due.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    /// <summary>Marker left in place of a scrubbed one-time-credential body.</summary>
    public const string ScrubbedBodyMarker = "[inhoud met eenmalige link verwijderd na afhandeling]";

    /// <summary>
    /// C3 token-persistence hygiene: once delivery is decided, an activation/reset link has no
    /// business staying in the database. The row itself remains as the audit trail of the send.
    /// </summary>
    private static void ScrubCredentialBody(OutboxMessage message)
    {
        if (MessageKinds.CarriesOneTimeCredential(message.Kind))
        {
            message.Body = ScrubbedBodyMarker;
        }
    }

    private async Task HandlePermanentFailureAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Alert the back office in-app. The dispatcher runs tenant-agnostic (no request scope),
        // so the rows are written directly for the message's own tenant; Critical bypasses
        // preferences by definition.
        // H-14 (I-3): back office only — a customer-linked account holding a legacy orders.manage
        // grant is not staff and must never be told that a customer mail failed.
        var holders = await (from ur in _dbContext.UserRoles.AsNoTracking()
                             join u in _dbContext.Users.AsNoTracking().Where(u => u.TenantId == message.TenantId && u.IsActive)
                                 .Where(PortalPermissionScope.InternalIdentityOnly)
                                 on ur.UserId equals u.Id
                             join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == message.TenantId && r.IsActive)
                                 on ur.RoleId equals r.Id
                             join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
                             join p in _dbContext.Permissions.AsNoTracking().Where(p => p.Code == PermissionCodes.OrdersManage)
                                 on rp.PermissionId equals p.Id
                             select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
        var (category, severity) = NotificationTypeCatalog.Resolve("customer_notification_failed");
        foreach (var holder in holders)
        {
            _dbContext.Add(new Notification
            {
                Id = Guid.NewGuid(),
                TenantId = message.TenantId,
                UserId = holder,
                Type = "customer_notification_failed",
                Category = category,
                Severity = severity,
                Title = "Bericht kon niet worden verzonden",
                Message = $"{message.Channel}-bericht '{message.Kind}' aan {message.RecipientAddress} is definitief mislukt: {message.FailureReason}",
                LinkPath = "/messaging",
            });
        }

        // One-shot fallback to the other channel when the profile asks for it.
        if (message.FallbackOfMessageId is not null)
        {
            return; // a fallback never chains into another fallback
        }

        var profile = await _dbContext.MessagingProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == message.TenantId && p.OwnerType == message.OwnerType
                                      && p.OwnerId == message.OwnerId, cancellationToken);
        if (profile?.FallbackChannel is not { } fallbackChannel || fallbackChannel == message.Channel)
        {
            return;
        }

        var address = fallbackChannel == MessageChannel.Sms ? profile.PhoneNumber : profile.EmailAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        _dbContext.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = message.TenantId,
            Channel = fallbackChannel,
            Kind = message.Kind,
            OwnerType = message.OwnerType,
            OwnerId = message.OwnerId,
            RecipientAddress = address,
            RecipientName = message.RecipientName,
            Language = message.Language,
            // SMS fallback of an e-mail: reuse the body without subject (built-in SMS templates
            // are shorter, but the original body remains the trustworthy rendered content).
            Subject = fallbackChannel == MessageChannel.Email ? message.Subject ?? message.Kind : null,
            Body = message.Body,
            Status = OutboxStatus.Pending,
            IdempotencyKey = $"{message.IdempotencyKey}:fallback",
            RelatedEntityType = message.RelatedEntityType,
            RelatedEntityId = message.RelatedEntityId,
            FallbackOfMessageId = message.Id,
        });
    }
}
