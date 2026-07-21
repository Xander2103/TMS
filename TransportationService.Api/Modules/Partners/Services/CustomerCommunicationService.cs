using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Partners.Services;

public record CustomerCommunicationRuleDto(
    Guid Id,
    CustomerCommunicationType Type,
    string? CustomTypeLabel,
    string Channel,
    string? CcEmail,
    string? LanguageCode,
    Guid? FallbackContactId,
    bool IsActive,
    IReadOnlyList<Guid> ContactIds);

public record SaveCustomerCommunicationRuleRequest(
    CustomerCommunicationType Type,
    string? CustomTypeLabel,
    string? CcEmail,
    string? LanguageCode,
    Guid? FallbackContactId,
    bool IsActive,
    IReadOnlyList<Guid> ContactIds);

/// <summary>A resolved recipient for a communication type (real contact data at call time).</summary>
public record CommunicationRecipientDto(Guid ContactId, string Name, string Email, string? LanguageCode, bool IsFallback);

public interface ICustomerCommunicationService
{
    Task<IReadOnlyList<CustomerCommunicationRuleDto>?> ListAsync(Guid customerId, CancellationToken cancellationToken);
    Task<CustomerCommunicationRuleDto?> CreateAsync(Guid customerId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken);
    Task<CustomerCommunicationRuleDto?> UpdateAsync(Guid customerId, Guid ruleId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid customerId, Guid ruleId, CancellationToken cancellationToken);

    /// <summary>
    /// Recipients for a communication type: emails of the active linked contacts of every
    /// active rule of that type; when a rule yields none, its fallback contact (if usable).
    /// This is the seam future senders (invoice mail, planning alerts) consume.
    /// </summary>
    Task<IReadOnlyList<CommunicationRecipientDto>> ResolveRecipientsAsync(Guid customerId, CustomerCommunicationType type, CancellationToken cancellationToken);
}

public class CustomerCommunicationService : ICustomerCommunicationService
{
    private const string EntityType = "Customer";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public CustomerCommunicationService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<CustomerCommunicationRuleDto>?> ListAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        var rules = await _dbContext.CustomerCommunicationRules
            .Include(r => r.Contacts)
            .Where(r => r.TenantId == _tenantContext.TenantId && r.CustomerId == customerId)
            .OrderBy(r => r.Type)
            .ToListAsync(cancellationToken);
        return rules.Select(Map).ToList();
    }

    public async Task<CustomerCommunicationRuleDto?> CreateAsync(
        Guid customerId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        await ValidateAsync(customerId, request, cancellationToken);

        var rule = new CustomerCommunicationRule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customerId,
        };
        Apply(rule, request);
        foreach (var contactId in request.ContactIds.Distinct())
        {
            _dbContext.CustomerCommunicationRuleContacts.Add(new CustomerCommunicationRuleContact
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                RuleId = rule.Id,
                ContactId = contactId,
            });
        }

        _dbContext.CustomerCommunicationRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "CommunicationRuleAdded", null,
            new { rule.Id, rule.Type, ContactCount = request.ContactIds.Count }, cancellationToken);

        return Map(await ReloadAsync(rule.Id, cancellationToken));
    }

    public async Task<CustomerCommunicationRuleDto?> UpdateAsync(
        Guid customerId, Guid ruleId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.CustomerCommunicationRules
            .Include(r => r.Contacts)
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.CustomerId == customerId && r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        await ValidateAsync(customerId, request, cancellationToken);

        var before = new { rule.Type, rule.IsActive, ContactCount = rule.Contacts.Count };
        Apply(rule, request);

        var wanted = request.ContactIds.Distinct().ToHashSet();
        var removed = rule.Contacts.Where(c => !wanted.Contains(c.ContactId)).ToList();
        _dbContext.RemoveRange(removed);
        var existing = rule.Contacts.Select(c => c.ContactId).ToHashSet();
        foreach (var contactId in wanted.Except(existing))
        {
            _dbContext.CustomerCommunicationRuleContacts.Add(new CustomerCommunicationRuleContact
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                RuleId = rule.Id,
                ContactId = contactId,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "CommunicationRuleChanged", before,
            new { rule.Id, rule.Type, rule.IsActive, ContactCount = wanted.Count }, cancellationToken);

        return Map(await ReloadAsync(rule.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid customerId, Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.CustomerCommunicationRules
            .Include(r => r.Contacts)
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.CustomerId == customerId && r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        _dbContext.RemoveRange(rule.Contacts);
        _dbContext.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "CommunicationRuleRemoved",
            new { rule.Id, rule.Type }, null, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<CommunicationRecipientDto>> ResolveRecipientsAsync(
        Guid customerId, CustomerCommunicationType type, CancellationToken cancellationToken)
    {
        var rules = await _dbContext.CustomerCommunicationRules
            .Include(r => r.Contacts)
            .Where(r => r.TenantId == _tenantContext.TenantId && r.CustomerId == customerId && r.Type == type && r.IsActive)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return [];
        }

        var contactIds = rules.SelectMany(r => r.Contacts.Select(c => c.ContactId))
            .Concat(rules.Where(r => r.FallbackContactId is not null).Select(r => r.FallbackContactId!.Value))
            .Distinct()
            .ToList();
        var contacts = await _dbContext.CustomerContacts
            .Where(c => c.TenantId == _tenantContext.TenantId && contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var recipients = new List<CommunicationRecipientDto>();
        foreach (var rule in rules)
        {
            var resolved = rule.Contacts
                .Select(link => contacts.GetValueOrDefault(link.ContactId))
                .Where(c => c is { IsActive: true } && !string.IsNullOrWhiteSpace(c.Email))
                .Select(c => new CommunicationRecipientDto(
                    c!.Id, DisplayName(c), c.Email!, rule.LanguageCode ?? c.PreferredLanguageCode, IsFallback: false))
                .ToList();

            if (resolved.Count == 0 && rule.FallbackContactId is { } fallbackId
                && contacts.GetValueOrDefault(fallbackId) is { IsActive: true } fallback
                && !string.IsNullOrWhiteSpace(fallback.Email))
            {
                resolved.Add(new CommunicationRecipientDto(
                    fallback.Id, DisplayName(fallback), fallback.Email!, rule.LanguageCode ?? fallback.PreferredLanguageCode, IsFallback: true));
            }

            recipients.AddRange(resolved);
        }

        return recipients.DistinctBy(r => (r.ContactId, r.IsFallback)).ToList();
    }

    private static string DisplayName(CustomerContact c) =>
        c.DisplayName ?? $"{c.FirstName} {c.LastName}".Trim();

    private async Task ValidateAsync(Guid customerId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken)
    {
        if (request.ContactIds.Count == 0)
        {
            throw new DomainValidationException("contactIds", "Koppel minstens één contactpersoon aan deze communicatieregel.");
        }

        if (request.Type == CustomerCommunicationType.Other && string.IsNullOrWhiteSpace(request.CustomTypeLabel))
        {
            throw new DomainValidationException("customTypeLabel", "Geef een omschrijving voor het communicatietype 'Andere'.");
        }

        if (!string.IsNullOrWhiteSpace(request.CcEmail) && !request.CcEmail.Contains('@'))
        {
            throw new DomainValidationException("ccEmail", "Het CC-adres is geen geldig e-mailadres.");
        }

        var allIds = request.ContactIds.Concat(request.FallbackContactId is { } f ? [f] : Array.Empty<Guid>()).Distinct().ToList();
        var known = await _dbContext.CustomerContacts
            .CountAsync(c => c.TenantId == _tenantContext.TenantId && c.CustomerId == customerId && allIds.Contains(c.Id), cancellationToken);
        if (known != allIds.Count)
        {
            throw new DomainValidationException("contactIds", "Eén of meer gekozen contactpersonen horen niet bij deze klant.");
        }
    }

    private static void Apply(CustomerCommunicationRule rule, SaveCustomerCommunicationRuleRequest request)
    {
        rule.Type = request.Type;
        rule.CustomTypeLabel = string.IsNullOrWhiteSpace(request.CustomTypeLabel) ? null : request.CustomTypeLabel.Trim();
        rule.CcEmail = string.IsNullOrWhiteSpace(request.CcEmail) ? null : request.CcEmail.Trim();
        rule.LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? null : request.LanguageCode.Trim().ToLowerInvariant();
        rule.FallbackContactId = request.FallbackContactId;
        rule.IsActive = request.IsActive;
    }

    private async Task<CustomerCommunicationRule> ReloadAsync(Guid ruleId, CancellationToken cancellationToken) =>
        await _dbContext.CustomerCommunicationRules
            .Include(r => r.Contacts)
            .FirstAsync(r => r.Id == ruleId, cancellationToken);

    private static CustomerCommunicationRuleDto Map(CustomerCommunicationRule r) => new(
        r.Id, r.Type, r.CustomTypeLabel, r.Channel, r.CcEmail, r.LanguageCode,
        r.FallbackContactId, r.IsActive,
        r.Contacts.Where(c => !c.IsDeleted).Select(c => c.ContactId).ToList());

    private Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken cancellationToken) =>
        _dbContext.Customers.AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken);
}
