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
///
/// <para><b>Delivery semantics (H2): at-most-once per claim.</b> Every row is first CLAIMED with a
/// conditional update (<c>Pending → Sending WHERE Status = 'Pending'</c>), so two dispatchers (a
/// second API instance, an overlapping run) can never both own, and therefore never both send,
/// the same row: the loser's update touches 0 rows and it moves on. The outcome is persisted
/// immediately after EACH send, so a crash, SIGTERM or DB blip mid-batch affects at most the one
/// message that was in flight, never the ones before it.</para>
///
/// <para>A row still <see cref="OutboxStatus.Sending"/> after <see cref="StaleClaimAfter"/> was
/// interrupted between "handed to the provider" and "outcome saved": the SMTP server may well
/// have accepted it. Customer mail must not be duplicated silently, so such a row is NOT resent
/// but moved to <see cref="OutboxStatus.AwaitingReview"/> with an explanatory reason; a dispatcher
/// releases it (send again) or rejects it in the existing review queue. The trade-off is
/// deliberate: a rare interrupted send costs a human decision instead of a possible duplicate
/// invoice/POD mail (at-least-once would resend automatically). <see cref="OutboxMessage.AttemptCount"/>
/// counts every claim, so repeated interruptions stay visible, and the SMTP provider stamps a
/// stable Message-ID per row so a released duplicate can be recognised downstream.</para>
///
/// <para>Rows are materialised ONE BY ONE after their claim. A row that cannot be read (e.g. an
/// unknown OwnerType/Channel value written by hand or by an older release) is quarantined as
/// <see cref="OutboxStatus.Failed"/> with a reason and the batch continues: a single poison row
/// can no longer stop all mail.</para>
/// </summary>
public class MessageDispatcher
{
    private const int MaxAttempts = 5;
    private const int FailureReasonMaxLength = 1000;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(5);

    /// <summary>A claim older than this is considered interrupted. SMTP itself times out after
    /// 30 s per operation, so a live send never gets near it.</summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(10);

    public const string StaleClaimReason =
        "Verzending onderbroken: het bericht is mogelijk al verzonden. Controleer (bv. in het "
        + "SMTP-logboek of bij de ontvanger) en geef het vrij om opnieuw te verzenden, of wijs het af.";

    public const string ChannelNotConfiguredReason =
        "Sms-verzending is niet geconfigureerd (geen sms-provider); het bericht is niet verzonden.";

    private readonly TransportationDbContext _dbContext;
    private readonly IMessageChannelProvider _emailProvider;
    private readonly IMessageChannelProvider _smsProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public MessageDispatcher(
        TransportationDbContext dbContext,
        IMessageChannelProvider emailProvider,
        IMessageChannelProvider smsProvider,
        TimeProvider timeProvider,
        ILogger<MessageDispatcher>? logger = null)
    {
        _dbContext = dbContext;
        _emailProvider = emailProvider;
        _smsProvider = smsProvider;
        _timeProvider = timeProvider;
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    public async Task<int> DispatchPendingAsync(int max, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ReviewStaleClaimsAsync(now, cancellationToken);

        // Ids only: a projection never materialises OwnerType/Channel, so one unreadable row
        // cannot fail the selection of the whole batch.
        var dueIds = await _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .Take(Math.Clamp(max, 1, 100))
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var id in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimToken = Guid.NewGuid();
            if (!await TryClaimAsync(id, claimToken, now, cancellationToken))
            {
                continue; // another dispatcher owns (or already finished) this row
            }

            // The claim was written with a set-based update; an instance this context already
            // tracks would shadow the claimed values (identity resolution), so read it fresh.
            foreach (var stale in _dbContext.ChangeTracker.Entries<OutboxMessage>()
                         .Where(e => e.Entity.Id == id).ToList())
            {
                stale.State = EntityState.Detached;
            }

            OutboxMessage? message;
            try
            {
                message = await _dbContext.OutboxMessages
                    .FirstOrDefaultAsync(m => m.Id == id && m.ClaimToken == claimToken, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await QuarantineUnreadableAsync(id, claimToken, exception, now, cancellationToken);
                continue;
            }

            if (message is null)
            {
                continue;
            }

            try
            {
                if (await ProcessClaimedAsync(message, now, cancellationToken))
                {
                    sent += 1;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Persisting the outcome failed (DB blip, constraint). The row keeps its Sending
                // claim and is routed to review once stale, never blindly resent. Detach so the
                // next message's save does not retry this broken state.
                _logger.LogError(exception,
                    "Outbox message {MessageId} could not be finalised; it stays claimed and will be held for review.",
                    message.Id);
                DetachPendingChanges();
            }
        }

        return sent;
    }

    /// <summary>Atomic claim: exactly one caller turns a Pending row into Sending.</summary>
    private async Task<bool> TryClaimAsync(Guid id, Guid claimToken, DateTime now, CancellationToken cancellationToken)
    {
        var claimed = await _dbContext.OutboxMessages
            .Where(m => m.Id == id && m.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, OutboxStatus.Sending)
                .SetProperty(m => m.ClaimToken, claimToken)
                .SetProperty(m => m.ClaimedAt, now)
                .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1)
                .SetProperty(m => m.LastAttemptAt, now)
                .SetProperty(m => m.UpdatedAt, now), cancellationToken);
        return claimed == 1;
    }

    /// <summary>Sends one claimed message and persists its outcome before returning.</summary>
    private async Task<bool> ProcessClaimedAsync(OutboxMessage message, DateTime now, CancellationToken cancellationToken)
    {
        // EX-02, last line of defence: a body edited/queued around the outbox guard with a literal
        // {{placeholder}} still never leaves the building.
        if (MessageTemplateRenderer.Unresolved(message.Subject, message.Body) is { Count: > 0 } unresolved)
        {
            message.Status = OutboxStatus.Suppressed;
            message.FailureReason = MessageOutboxService.UnresolvedReason(unresolved);
            message.ClaimToken = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        var provider = message.Channel == MessageChannel.Email ? _emailProvider : _smsProvider;
        if (provider is IDisabledChannelProvider)
        {
            // CFG-05: no provider for this channel. An honest, final "not sent" instead of five
            // doomed retries and a Critical alert per message.
            message.Status = OutboxStatus.Suppressed;
            message.FailureReason = ChannelNotConfiguredReason;
            message.ClaimToken = null;
            ScrubCredentialBody(message);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        try
        {
            await provider.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            message.FailureReason = Truncate(exception.Message, FailureReasonMaxLength);
            message.ClaimToken = null;
            _logger.LogWarning(
                "Outbox message {MessageId} ({Channel}, {Kind}) attempt {Attempt} failed: {Reason}",
                message.Id, message.Channel, message.Kind, message.AttemptCount, message.FailureReason);
            if (message.AttemptCount >= MaxAttempts)
            {
                message.Status = OutboxStatus.Failed;
                ScrubCredentialBody(message);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await AlertPermanentFailureAsync(message, cancellationToken);
            }
            else
            {
                // 5, 10, 20, 40 minutes.
                message.Status = OutboxStatus.Pending;
                message.NextAttemptAt = now + BaseBackoff * Math.Pow(2, message.AttemptCount - 1);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return false;
        }

        message.Status = OutboxStatus.Sent;
        message.SentAt = now;
        message.FailureReason = null;
        message.ClaimToken = null;
        ScrubCredentialBody(message);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// The alert and fallback of a permanent failure are written in their own unit of work: if
    /// they cannot be saved, the (already persisted) Failed state stands and the batch goes on.
    /// </summary>
    private async Task AlertPermanentFailureAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await HandlePermanentFailureAsync(message, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception,
                "Permanent-failure alert/fallback for outbox message {MessageId} could not be saved.", message.Id);
            DetachPendingChanges();
        }
    }

    /// <summary>Interrupted sends (see the class remarks) go to human review, never resent blindly.</summary>
    private async Task ReviewStaleClaimsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now - StaleClaimAfter;
        var held = await _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Sending && (m.ClaimedAt == null || m.ClaimedAt < cutoff))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, OutboxStatus.AwaitingReview)
                .SetProperty(m => m.FailureReason, StaleClaimReason)
                .SetProperty(m => m.ClaimToken, (Guid?)null)
                .SetProperty(m => m.UpdatedAt, now), cancellationToken);
        if (held > 0)
        {
            _logger.LogWarning(
                "{Count} outbox message(s) were interrupted mid-send and are held for review (possibly already delivered).",
                held);
        }
    }

    /// <summary>A claimed row that cannot even be materialised is failed individually.</summary>
    private async Task QuarantineUnreadableAsync(
        Guid id, Guid claimToken, Exception exception, DateTime now, CancellationToken cancellationToken)
    {
        _logger.LogError(exception,
            "Outbox message {MessageId} cannot be read and is quarantined as Failed; the batch continues.", id);
        var reason = $"Ongeldige outboxrij, niet verzonden ({exception.GetType().Name}). Corrigeer of verwijder de rij.";
        try
        {
            await _dbContext.OutboxMessages
                .Where(m => m.Id == id && m.ClaimToken == claimToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, OutboxStatus.Failed)
                    .SetProperty(m => m.FailureReason, reason)
                    .SetProperty(m => m.ClaimToken, (Guid?)null)
                    .SetProperty(m => m.UpdatedAt, now), cancellationToken);
        }
        catch (Exception quarantineFailure) when (quarantineFailure is not OperationCanceledException)
        {
            // Left as Sending: the stale-claim rule parks it for review; it is never resent.
            _logger.LogError(quarantineFailure, "Outbox message {MessageId} could not be quarantined.", id);
        }
    }

    private void DetachPendingChanges()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

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
