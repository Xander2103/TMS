using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>One contact and the notification options they receive.</summary>
public record ContactSubscriptionsDto(Guid ContactId, IReadOnlyList<string> OptionKeys);

/// <summary>A recipient shown on the communication overview.</summary>
public record NotificationRecipientLineDto(
    Guid? ContactId,
    string Name,
    string? Email,
    /// <summary>True for a CC address or fallback contact — advanced routing, hidden by default.</summary>
    bool IsAdvanced,
    bool IsActive);

/// <summary>One row of the "who receives what?" overview.</summary>
public record NotificationOverviewLineDto(
    string OptionKey,
    CustomerNotificationGroup Group,
    IReadOnlyList<NotificationRecipientLineDto> Recipients);

public interface ICustomerContactSubscriptionService
{
    Task<ContactSubscriptionsDto?> GetForContactAsync(Guid customerId, Guid contactId, CancellationToken cancellationToken);
    Task<ContactSubscriptionsDto?> SetForContactAsync(Guid customerId, Guid contactId, IReadOnlyList<string> optionKeys, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationOverviewLineDto>?> GetOverviewAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>
/// Sprint 3: the contact-centric surface over the existing communication-rule engine.
///
/// A normal user says "Jan receives Planning and ETA". That is stored as exactly the rules the
/// engine already understands, so nothing downstream changes: rules keep their channel, CC
/// address, fallback contact, language override and custom label, and remain editable on the
/// advanced screen. This service only ever adds or removes the CONTACT LINK — it never
/// rewrites the advanced parts of a rule it did not create.
/// </summary>
public class CustomerContactSubscriptionService : ICustomerContactSubscriptionService
{
    private const string EntityType = "Customer";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public CustomerContactSubscriptionService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<CustomerCommunicationRule> Rules(Guid customerId) =>
        _dbContext.CustomerCommunicationRules
            .Include(r => r.Contacts)
            .Where(r => r.TenantId == _tenantContext.TenantId && r.CustomerId == customerId);

    public async Task<ContactSubscriptionsDto?> GetForContactAsync(
        Guid customerId, Guid contactId, CancellationToken cancellationToken)
    {
        if (!await ContactExistsAsync(customerId, contactId, cancellationToken)) return null;

        var rules = await Rules(customerId).ToListAsync(cancellationToken);
        var subscribedTypes = rules
            .Where(r => r.IsActive && r.Contacts.Any(c => c.ContactId == contactId))
            .Select(r => r.Type)
            .ToHashSet();

        // An option counts as "on" as soon as the contact receives ANY of the types behind it,
        // which is what the single checkbox promised.
        var keys = CustomerNotificationCatalog.Options
            .Where(o => o.Types.Any(subscribedTypes.Contains))
            .Select(o => o.Key)
            .ToList();

        return new ContactSubscriptionsDto(contactId, keys);
    }

    public async Task<ContactSubscriptionsDto?> SetForContactAsync(
        Guid customerId, Guid contactId, IReadOnlyList<string> optionKeys, CancellationToken cancellationToken)
    {
        if (!await ContactExistsAsync(customerId, contactId, cancellationToken)) return null;

        var wanted = optionKeys
            .Select(CustomerNotificationCatalog.Find)
            .Where(o => o is not null)
            .Select(o => o!.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var before = (await GetForContactAsync(customerId, contactId, cancellationToken))!.OptionKeys;
        var currentlyOn = before.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = await Rules(customerId).ToListAsync(cancellationToken);

        // Links are added through the DbSet with an explicit RuleId rather than through the
        // parent's navigation: the rules were just re-queried into an already-tracked graph, and
        // relying on navigation fix-up there makes EF treat a brand-new link as Modified.
        var addedLinks = new HashSet<(Guid RuleId, Guid ContactId)>();

        foreach (var option in CustomerNotificationCatalog.Options)
        {
            var subscribe = wanted.Contains(option.Key);
            // Only options whose state actually changes are touched (audit fix): re-applying an
            // unchanged ON option would subscribe the contact to every type behind it, even if
            // an administrator had deliberately limited them to one on the advanced screen.
            if (subscribe == currentlyOn.Contains(option.Key)) continue;
            foreach (var type in option.Types)
            {
                var rule = rules.FirstOrDefault(r => r.Type == type);
                if (subscribe)
                {
                    rule = Subscribe(rule, customerId, type, contactId, addedLinks);
                    if (!rules.Contains(rule)) rules.Add(rule);
                }
                else if (rule is not null)
                {
                    Unsubscribe(rule, contactId);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = (await GetForContactAsync(customerId, contactId, cancellationToken))!;
        await _auditService.RecordAsync(EntityType, customerId.ToString(), "ContactNotificationsChanged",
            new { ContactId = contactId, OptionKeys = before },
            new { ContactId = contactId, after.OptionKeys }, cancellationToken);

        return after;
    }

    private CustomerCommunicationRule Subscribe(
        CustomerCommunicationRule? rule, Guid customerId, CustomerCommunicationType type, Guid contactId,
        HashSet<(Guid RuleId, Guid ContactId)> addedLinks)
    {
        if (rule is null)
        {
            rule = new CustomerCommunicationRule
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                CustomerId = customerId,
                Type = type,
                Channel = "Email",
                IsActive = true,
            };
            _dbContext.CustomerCommunicationRules.Add(rule);
        }
        else
        {
            // A rule that was switched off entirely would silently swallow the new subscription.
            rule.IsActive = true;
            if (rule.Contacts.Any(c => c.ContactId == contactId)) return rule;
        }

        if (!addedLinks.Add((rule.Id, contactId))) return rule;

        _dbContext.CustomerCommunicationRuleContacts.Add(new CustomerCommunicationRuleContact
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            RuleId = rule.Id,
            ContactId = contactId,
        });

        return rule;
    }

    private void Unsubscribe(CustomerCommunicationRule rule, Guid contactId)
    {
        var link = rule.Contacts.FirstOrDefault(c => c.ContactId == contactId);
        if (link is null) return;

        rule.Contacts.Remove(link);
        _dbContext.CustomerCommunicationRuleContacts.Remove(link);

        // An empty rule that still carries advanced routing (a CC mailbox or a fallback
        // contact) can still deliver, so it is kept. One with nothing left would be dead
        // configuration and is removed.
        var hasAdvancedDelivery = rule.FallbackContactId is not null || !string.IsNullOrWhiteSpace(rule.CcEmail);
        if (rule.Contacts.Count == 0 && !hasAdvancedDelivery)
        {
            _dbContext.CustomerCommunicationRules.Remove(rule);
        }
    }

    public async Task<IReadOnlyList<NotificationOverviewLineDto>?> GetOverviewAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var customerExists = await _dbContext.Customers
            .AnyAsync(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId, cancellationToken);
        if (!customerExists) return null;

        var rules = await Rules(customerId).ToListAsync(cancellationToken);
        var contactIds = rules
            .SelectMany(r => r.Contacts.Select(c => c.ContactId))
            .Concat(rules.Where(r => r.FallbackContactId is not null).Select(r => r.FallbackContactId!.Value))
            .Distinct()
            .ToList();
        var contacts = await _dbContext.CustomerContacts
            .Where(c => c.TenantId == _tenantContext.TenantId && contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var lines = new List<NotificationOverviewLineDto>();
        foreach (var option in CustomerNotificationCatalog.Options)
        {
            var recipients = new List<NotificationRecipientLineDto>();
            foreach (var rule in rules.Where(r => option.Types.Contains(r.Type) && r.IsActive))
            {
                foreach (var link in rule.Contacts)
                {
                    if (contacts.GetValueOrDefault(link.ContactId) is not { } contact) continue;
                    recipients.Add(new NotificationRecipientLineDto(
                        contact.Id, DisplayName(contact), contact.Email, IsAdvanced: false, contact.IsActive));
                }

                if (!string.IsNullOrWhiteSpace(rule.CcEmail))
                {
                    recipients.Add(new NotificationRecipientLineDto(null, rule.CcEmail!, rule.CcEmail, IsAdvanced: true, true));
                }

                if (rule.FallbackContactId is { } fallbackId
                    && contacts.GetValueOrDefault(fallbackId) is { } fallback)
                {
                    recipients.Add(new NotificationRecipientLineDto(
                        fallback.Id, DisplayName(fallback), fallback.Email, IsAdvanced: true, fallback.IsActive));
                }
            }

            lines.Add(new NotificationOverviewLineDto(
                option.Key, option.Group,
                recipients.DistinctBy(r => (r.ContactId, r.Email, r.IsAdvanced)).ToList()));
        }

        return lines;
    }

    private static string DisplayName(CustomerContact c) =>
        c.DisplayName ?? $"{c.FirstName} {c.LastName}".Trim();

    private Task<bool> ContactExistsAsync(Guid customerId, Guid contactId, CancellationToken cancellationToken) =>
        _dbContext.CustomerContacts.AnyAsync(
            c => c.TenantId == _tenantContext.TenantId && c.CustomerId == customerId && c.Id == contactId,
            cancellationToken);
}
