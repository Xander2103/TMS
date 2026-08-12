using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>Everything a producer knows: what happened, about whom, with which template tokens.
/// OwnerType/OwnerId still identify whose <see cref="MessagingProfile"/> governs quiet
/// hours/opt-outs/fallback; the three Override* fields let a caller (NotificationEventService)
/// supply an already-resolved recipient (e.g. a customer contact's own address, or a literal
/// explicit address with no natural owner) without that owner having to BE the message target —
/// resolution still prefers an explicit MessagingProfile override first.</summary>
public record MessageRequest(
    string Kind,
    MessageOwnerType OwnerType,
    Guid OwnerId,
    IReadOnlyDictionary<string, string> Tokens,
    string? RelatedEntityType,
    string? RelatedEntityId,
    string IdempotencyKey,
    string? OverrideAddress = null,
    string? OverrideName = null,
    string? OverrideLanguage = null,
    /// <summary>When set, template resolution consults this customer's overrides before the
    /// tenant defaults (see MessageTemplate.CustomerId).</summary>
    Guid? CustomerIdForTemplate = null);

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
        if (owner is null && string.IsNullOrWhiteSpace(request.OverrideAddress))
        {
            return new QueueResult(QueueOutcome.NoRecipient, Reason: "Eigenaar niet gevonden.");
        }

        var emailEnabled = profile?.EmailEnabled ?? true;
        var smsEnabled = profile?.SmsEnabled ?? false;
        var channel = emailEnabled ? MessageChannel.Email : smsEnabled ? MessageChannel.Sms : (MessageChannel?)null;

        var language = profile?.PreferredLanguage ?? request.OverrideLanguage ?? owner?.Language ?? "nl";
        var address = channel == MessageChannel.Sms
            ? profile?.PhoneNumber
            : profile?.EmailAddress ?? request.OverrideAddress ?? owner?.Email;

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

        var (subject, body) = await RenderAsync(
            request.Kind, channel ?? MessageChannel.Email, language, request.Tokens, request.CustomerIdForTemplate, cancellationToken);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel ?? MessageChannel.Email,
            Kind = request.Kind,
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            RecipientAddress = address ?? owner?.Email ?? "onbekend",
            RecipientName = request.OverrideName ?? owner?.Name,
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
        string kind, MessageChannel channel, string language, IReadOnlyDictionary<string, string> tokens,
        Guid? customerIdForTemplate, CancellationToken cancellationToken)
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

        var (subjectTemplate, bodyTemplate) = await ResolveTemplateAsync(kind, channel, language, customerIdForTemplate, cancellationToken);

        return (
            subjectTemplate is null ? null : MessageTemplateRenderer.Render(subjectTemplate, tokensWithDefaults),
            MessageTemplateRenderer.Render(bodyTemplate, tokensWithDefaults));
    }

    /// <summary>
    /// Resolution chain: (customer, kind, channel, language) → (customer, kind, channel, "nl") →
    /// (tenant, kind, channel, language) → (tenant, kind, channel, "nl") → built-in. A customer
    /// row is only consulted when the caller identifies one (customer-directed messages);
    /// internal/explicit recipients always resolve against the tenant-wide template.
    /// </summary>
    private async Task<(string? Subject, string Body)> ResolveTemplateAsync(
        string kind, MessageChannel channel, string language, Guid? customerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var candidates = await _dbContext.MessageTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Kind == kind && t.Channel == channel && t.IsActive
                        && (t.CustomerId == null || t.CustomerId == customerId))
            .ToListAsync(cancellationToken);

        MessageTemplate? Find(Guid? forCustomer, string forLanguage) =>
            candidates.FirstOrDefault(t => t.CustomerId == forCustomer && t.Language == forLanguage);

        var resolved =
            (customerId is { } id ? Find(id, language) : null)
            ?? (customerId is { } id2 ? Find(id2, "nl") : null)
            ?? Find(null, language)
            ?? Find(null, "nl");

        if (resolved is not null)
        {
            return (resolved.Subject, resolved.Body);
        }

        var builtIn = BuiltInMessageTemplates.Resolve(kind, channel, language);
        return (builtIn.Subject, builtIn.Body);
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
