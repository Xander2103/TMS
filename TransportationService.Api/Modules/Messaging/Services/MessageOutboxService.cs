using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>Everything a producer knows: what happened, about whom, with which template tokens.</summary>
public record MessageRequest(
    string Kind,
    MessageOwnerType OwnerType,
    Guid OwnerId,
    IReadOnlyDictionary<string, string> Tokens,
    string? RelatedEntityType,
    string? RelatedEntityId,
    string IdempotencyKey);

public enum QueueOutcome
{
    Queued,
    Suppressed,
    Duplicate,
    NoRecipient,
}

public record QueueResult(QueueOutcome Outcome, Guid? MessageId = null, string? Reason = null);

public interface IMessageOutboxService
{
    /// <summary>Resolves the owner's messaging profile, renders the template and queues (or
    /// suppresses, with reason) the message. Idempotent per (tenant, idempotency key).</summary>
    Task<QueueResult> QueueAsync(MessageRequest request, CancellationToken cancellationToken);
}

public class MessageOutboxService : IMessageOutboxService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public MessageOutboxService(TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<QueueResult> QueueAsync(MessageRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var duplicate = await _dbContext.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (duplicate)
        {
            return new QueueResult(QueueOutcome.Duplicate);
        }

        var profile = await _dbContext.MessagingProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.OwnerType == request.OwnerType
                                      && p.OwnerId == request.OwnerId, cancellationToken);

        var owner = await ResolveOwnerAsync(request.OwnerType, request.OwnerId, cancellationToken);
        if (owner is null)
        {
            return new QueueResult(QueueOutcome.NoRecipient, Reason: "Eigenaar niet gevonden.");
        }

        var emailEnabled = profile?.EmailEnabled ?? true;
        var smsEnabled = profile?.SmsEnabled ?? false;
        var channel = emailEnabled ? MessageChannel.Email : smsEnabled ? MessageChannel.Sms : (MessageChannel?)null;

        var language = profile?.PreferredLanguage ?? owner.Language ?? "nl";
        var address = channel == MessageChannel.Sms
            ? profile?.PhoneNumber
            : profile?.EmailAddress ?? owner.Email;

        string? suppressReason = null;
        if (channel is null)
        {
            suppressReason = "Alle kanalen staan uit voor deze ontvanger.";
        }
        else if (!KindEnabled(profile, request.Kind))
        {
            suppressReason = $"Berichttype '{request.Kind}' staat uit voor deze ontvanger.";
        }
        else if (string.IsNullOrWhiteSpace(address))
        {
            return new QueueResult(QueueOutcome.NoRecipient, Reason: "Geen adres/telefoonnummer bekend.");
        }

        var (subject, body) = await RenderAsync(request.Kind, channel ?? MessageChannel.Email, language, request.Tokens, cancellationToken);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel ?? MessageChannel.Email,
            Kind = request.Kind,
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            RecipientAddress = address ?? owner.Email ?? "onbekend",
            RecipientName = owner.Name,
            Language = language,
            Subject = subject,
            Body = body,
            Status = suppressReason is null ? OutboxStatus.Pending : OutboxStatus.Suppressed,
            FailureReason = suppressReason,
            NextAttemptAt = suppressReason is null
                ? NextAllowedMoment(profile, _timeProvider.GetUtcNow().UtcDateTime)
                : null,
            IdempotencyKey = request.IdempotencyKey,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
        };
        _dbContext.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return suppressReason is null
            ? new QueueResult(QueueOutcome.Queued, message.Id)
            : new QueueResult(QueueOutcome.Suppressed, message.Id, suppressReason);
    }

    private static bool KindEnabled(MessagingProfile? profile, string kind)
    {
        if (profile?.EnabledKindsJson is not { } json)
        {
            return true;
        }

        try
        {
            var kinds = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return kinds.Contains(kind, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>Quiet hours push delivery to the end of the window (same-day windows only).</summary>
    internal static DateTime? NextAllowedMoment(MessagingProfile? profile, DateTime now)
    {
        if (profile?.QuietHoursFrom is not { } from || profile.QuietHoursTo is not { } to)
        {
            return null;
        }

        var time = TimeOnly.FromDateTime(now);
        var inWindow = from <= to
            ? time >= from && time < to
            : time >= from || time < to; // window over midnight
        if (!inWindow)
        {
            return null;
        }

        var endToday = now.Date.Add(to.ToTimeSpan());
        return endToday > now ? endToday : endToday.AddDays(1);
    }

    private async Task<(string? Subject, string Body)> RenderAsync(
        string kind, MessageChannel channel, string language,
        IReadOnlyDictionary<string, string> tokens, CancellationToken cancellationToken)
    {
        var tokensWithDefaults = new Dictionary<string, string>(tokens);
        if (!tokensWithDefaults.ContainsKey("companyName"))
        {
            var companyName = await _dbContext.Tenants.AsNoTracking()
                .Where(t => t.Id == _tenantContext.TenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "TransportationService";
            tokensWithDefaults["companyName"] = companyName;
        }

        var custom = await _dbContext.MessageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Kind == kind
                                      && t.Channel == channel && t.Language == language && t.IsActive, cancellationToken);

        string? subjectTemplate;
        string bodyTemplate;
        if (custom is not null)
        {
            subjectTemplate = custom.Subject;
            bodyTemplate = custom.Body;
        }
        else
        {
            var builtIn = BuiltInMessageTemplates.Resolve(kind, channel);
            subjectTemplate = builtIn.Subject;
            bodyTemplate = builtIn.Body;
        }

        return (
            subjectTemplate is null ? null : MessageTemplateRenderer.Render(subjectTemplate, tokensWithDefaults),
            MessageTemplateRenderer.Render(bodyTemplate, tokensWithDefaults));
    }

    private sealed record OwnerInfo(string? Email, string? Language, string? Name);

    private async Task<OwnerInfo?> ResolveOwnerAsync(
        MessageOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (ownerType == MessageOwnerType.Customer)
        {
            return await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == ownerId && c.TenantId == tenantId)
                .Select(c => new OwnerInfo(c.Email, c.DefaultLanguageCode, c.Name))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == ownerId && e.TenantId == tenantId)
            .Select(e => new OwnerInfo(e.Email, e.PreferredLanguageCode, e.FirstName + " " + e.LastName))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
