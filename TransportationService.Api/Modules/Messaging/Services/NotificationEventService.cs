using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Everything a call site knows about one occurrence of a cataloged event. TenantId is
/// deliberately absent: this service always trusts the injected <see cref="ITenantContext"/>,
/// exactly like <see cref="MessageOutboxService"/> — request-scoped call sites get it from DI,
/// and tenant-agnostic sweeps (expiry producers) construct a throwaway
/// <see cref="DevTenantContext"/> per tenant, same as those producers already do for the outbox.
/// </summary>
public sealed record NotificationEventContext(
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, string> Tokens)
{
    public Guid? CustomerId { get; init; }

    /// <summary>The event's subject employee (order's driver for order events, the affected
    /// employee for HR events) — see <see cref="NotificationRecipientType.Driver"/>.</summary>
    public Guid? EmployeeId { get; init; }

    /// <summary>In-app notification link (may carry a "#id" dedupe-marker fragment for
    /// producers that de-duplicate by scanning it, e.g. the expiry sweep).</summary>
    public string? LinkPath { get; init; }

    /// <summary>Pre-phrased Dutch in-app message; falls back to a generic token dump when absent.</summary>
    public string? InAppMessage { get; init; }

    /// <summary>Overrides the catalog label as the in-app notification title.</summary>
    public string? InAppTitle { get; init; }
}

public interface INotificationEventService
{
    /// <summary>
    /// Resolves the rule (or catalog defaults), the customer override, recipients and language,
    /// then queues an outbox row per e-mail recipient and an in-app notification per in-app
    /// recipient. Must be called AFTER the business save — never inside the same transaction;
    /// each queue/notify call does its own SaveChanges (same pattern as the existing producers).
    /// </summary>
    Task PublishAsync(string eventKey, NotificationEventContext context, CancellationToken cancellationToken);
}

public class NotificationEventService : INotificationEventService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IMessageOutboxService _messageOutbox;
    private readonly INotificationService _notificationService;
    private readonly ICustomerCommunicationService _communicationService;
    private readonly ILogger<NotificationEventService> _logger;

    public NotificationEventService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IMessageOutboxService messageOutbox,
        INotificationService notificationService,
        ICustomerCommunicationService communicationService,
        ILogger<NotificationEventService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _messageOutbox = messageOutbox;
        _notificationService = notificationService;
        _communicationService = communicationService;
        _logger = logger;
    }

    private sealed record EmailTarget(
        MessageOwnerType OwnerType, Guid OwnerId, string Address, string? Name,
        string? PreferredLanguage, Guid? CustomerIdForTemplate);

    public async Task PublishAsync(string eventKey, NotificationEventContext context, CancellationToken cancellationToken)
    {
        var info = NotificationEventCatalog.Resolve(eventKey);
        if (info is null)
        {
            _logger.LogWarning("PublishAsync: unknown event key '{EventKey}' — nothing sent.", eventKey);
            return;
        }

        var tenantId = _tenantContext.TenantId;

        var rule = await _dbContext.NotificationRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EventKey == eventKey, cancellationToken);

        if (!(rule?.Enabled ?? true))
        {
            return;
        }

        if (rule?.AllowCustomerOverride == true && context.CustomerId is { } overrideCustomerId)
        {
            var customerOverride = await _dbContext.CustomerNotificationOverrides.AsNoTracking()
                .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.CustomerId == overrideCustomerId
                                          && o.EventKey == eventKey, cancellationToken);
            if (customerOverride?.Enabled == false)
            {
                return;
            }
        }

        var inAppEnabled = rule?.InAppEnabled ?? info.DefaultInApp;
        var emailEnabled = rule?.EmailEnabled ?? info.DefaultEmail;
        // P9: sensitive kinds hold their CUSTOMER mail for dispatcher review (rule overrides
        // the catalog default); internal staff mail is never held.
        var requiresReview = rule?.RequiresReview ?? info.DefaultRequiresReview;
        var recipients = ParseRecipients(rule?.RecipientsJson) ?? info.DefaultRecipients;

        if (!inAppEnabled && !emailEnabled)
        {
            return;
        }

        // Fallback semantics: a CustomerPrimaryContact spec only fires when no earlier
        // CustomerCommunicationRule spec in the same list produced a recipient. That is how the
        // catalog expresses "the configured contacts, or else the primary contact" without
        // mailing both. Addresses are de-duplicated across specs as well.
        var ruleResolvedCustomerRecipient = false;
        var queuedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in recipients)
        {
            if (emailEnabled)
            {
                if (spec.Type == NotificationRecipientType.CustomerPrimaryContact && ruleResolvedCustomerRecipient)
                {
                    continue;
                }

                var targets = await ResolveEmailTargetsAsync(spec, context, tenantId, cancellationToken);
                if (spec.Type == NotificationRecipientType.CustomerCommunicationRule && targets.Count > 0)
                {
                    ruleResolvedCustomerRecipient = true;
                }

                foreach (var target in targets)
                {
                    if (!queuedAddresses.Add(target.Address))
                    {
                        continue;
                    }

                    var language = await ResolveLanguageAsync(target.PreferredLanguage, target.CustomerIdForTemplate, tenantId, cancellationToken);
                    var idempotencyKey = $"{eventKey}:{context.EntityType}:{context.EntityId}:{target.Address}";
                    await _messageOutbox.QueueAsync(new MessageRequest(
                        info.MessageKind, target.OwnerType, target.OwnerId, context.Tokens,
                        context.EntityType, context.EntityId, idempotencyKey,
                        OverrideAddress: target.Address, OverrideName: target.Name, OverrideLanguage: language,
                        CustomerIdForTemplate: target.CustomerIdForTemplate,
                        RequiresReview: requiresReview && target.OwnerType == MessageOwnerType.Customer), cancellationToken);
                }
            }

            if (inAppEnabled)
            {
                await PublishInAppAsync(spec, eventKey, info, context, tenantId, cancellationToken);
            }
        }
    }

    // --- in-app fan-out ---

    private async Task PublishInAppAsync(
        RecipientSpec spec, string eventKey, NotificationEventInfo info, NotificationEventContext context,
        Guid tenantId, CancellationToken cancellationToken)
    {
        var title = context.InAppTitle ?? info.Label;
        var message = context.InAppMessage ?? DefaultMessage(context.Tokens);

        switch (spec.Type)
        {
            case NotificationRecipientType.InternalPermission when !string.IsNullOrWhiteSpace(spec.Value):
                await _notificationService.NotifyPermissionHoldersAsync(
                    spec.Value, eventKey, title, message, context.LinkPath, cancellationToken);
                break;

            case NotificationRecipientType.InternalRole when !string.IsNullOrWhiteSpace(spec.Value):
                var roleIds = await _dbContext.Roles.AsNoTracking()
                    .Where(r => r.TenantId == tenantId && r.IsActive && r.TemplateCode == spec.Value)
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);
                foreach (var roleId in roleIds)
                {
                    await _notificationService.NotifyRoleAsync(roleId, eventKey, title, message, context.LinkPath, cancellationToken);
                }

                break;

            case NotificationRecipientType.Driver when context.EmployeeId is { } employeeId:
                var userId = await _dbContext.Users.AsNoTracking()
                    .Where(u => u.TenantId == tenantId && u.EmployeeId == employeeId && u.IsActive)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (userId is not null)
                {
                    await _notificationService.NotifyAsync(userId, eventKey, title, message, context.LinkPath, cancellationToken);
                }

                break;

            // Customer-facing recipient types have no linked in-app user; skip silently.
            case NotificationRecipientType.CustomerPrimaryContact:
            case NotificationRecipientType.CustomerCommunicationRule:
            case NotificationRecipientType.ExplicitEmail:
            case NotificationRecipientType.Driver:
                break;
        }
    }

    // --- e-mail recipient resolution ---

    private async Task<IReadOnlyList<EmailTarget>> ResolveEmailTargetsAsync(
        RecipientSpec spec, NotificationEventContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        switch (spec.Type)
        {
            case NotificationRecipientType.CustomerPrimaryContact:
                return await ResolveCustomerPrimaryContactAsync(context.CustomerId, tenantId, cancellationToken);

            case NotificationRecipientType.CustomerCommunicationRule:
                return await ResolveCommunicationRuleAsync(spec.Value, context.CustomerId, tenantId, cancellationToken);

            case NotificationRecipientType.InternalPermission:
                return string.IsNullOrWhiteSpace(spec.Value)
                    ? []
                    : await ResolveInternalAsync(spec.Value, tenantId, byPermission: true, cancellationToken);

            case NotificationRecipientType.InternalRole:
                return string.IsNullOrWhiteSpace(spec.Value)
                    ? []
                    : await ResolveInternalAsync(spec.Value, tenantId, byPermission: false, cancellationToken);

            case NotificationRecipientType.ExplicitEmail:
                return string.IsNullOrWhiteSpace(spec.Value)
                    ? []
                    : [new EmailTarget(MessageOwnerType.Employee, Guid.Empty, spec.Value, null, null, null)];

            case NotificationRecipientType.Driver:
                return await ResolveDriverAsync(context.EmployeeId, tenantId, cancellationToken);

            default:
                return [];
        }
    }

    private async Task<IReadOnlyList<EmailTarget>> ResolveCustomerPrimaryContactAsync(
        Guid? customerId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id)
        {
            return [];
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == id)
            .Select(c => new { c.Email, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return [];
        }

        var contact = await _dbContext.CustomerContacts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CustomerId == id && c.IsPrimary && c.IsActive
                        && c.Email != null && c.Email != "")
            .Select(c => new { c.Email, c.FirstName, c.LastName, c.DisplayName, c.PreferredLanguageCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (contact is not null)
        {
            var name = contact.DisplayName ?? $"{contact.FirstName} {contact.LastName}".Trim();
            return [new EmailTarget(MessageOwnerType.Customer, id, contact.Email!, name, contact.PreferredLanguageCode, id)];
        }

        return string.IsNullOrWhiteSpace(customer.Email)
            ? []
            : [new EmailTarget(MessageOwnerType.Customer, id, customer.Email, customer.Name, null, id)];
    }

    private async Task<IReadOnlyList<EmailTarget>> ResolveCommunicationRuleAsync(
        string? typeValue, Guid? customerId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id || string.IsNullOrWhiteSpace(typeValue)
            || !TransportationService.Api.Common.EnumParsing.TryParseDefined<CustomerCommunicationType>(typeValue, out var type))
        {
            return [];
        }

        var recipients = await _communicationService.ResolveRecipientsAsync(id, type, cancellationToken);
        return recipients
            .Select(r => new EmailTarget(MessageOwnerType.Customer, id, r.Email, r.Name, r.LanguageCode, (Guid?)id))
            .ToList();
    }

    /// <summary>Shared internal-staff lookup for both InternalPermission and InternalRole. Written
    /// as two explicit query shapes (not a generic Expression) to keep the SQL translation simple.</summary>
    private async Task<IReadOnlyList<EmailTarget>> ResolveInternalAsync(
        string value, Guid tenantId, bool byPermission, CancellationToken cancellationToken)
    {
        // H-14 (I-3): "internal staff" is an identity class, not just a role membership. A
        // customer-linked account carrying a legacy internal grant is refused every internal
        // endpoint, so it must not be mailed internal traffic either — this is the outbound-e-mail
        // twin of the guard in NotificationService.
        var staff = _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Where(PortalPermissionScope.InternalIdentityOnly);

        var rows = byPermission
            ? await (from ur in _dbContext.UserRoles.AsNoTracking()
                     join u in staff on ur.UserId equals u.Id
                     join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive) on ur.RoleId equals r.Id
                     join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
                     join p in _dbContext.Permissions.AsNoTracking().Where(p => p.Code == value) on rp.PermissionId equals p.Id
                     select new { u.Id, u.Email, u.FirstName, u.LastName, u.EmployeeId })
                .Distinct()
                .ToListAsync(cancellationToken)
            : await (from ur in _dbContext.UserRoles.AsNoTracking()
                     join u in staff on ur.UserId equals u.Id
                     join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive && r.TemplateCode == value)
                         on ur.RoleId equals r.Id
                     select new { u.Id, u.Email, u.FirstName, u.LastName, u.EmployeeId })
                .Distinct()
                .ToListAsync(cancellationToken);

        var employeeIds = rows.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var languageByEmployee = employeeIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.PreferredLanguageCode, cancellationToken);

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => new EmailTarget(
                MessageOwnerType.Employee, r.EmployeeId ?? Guid.Empty, r.Email, $"{r.FirstName} {r.LastName}".Trim(),
                r.EmployeeId is { } eid ? languageByEmployee.GetValueOrDefault(eid) : null, null))
            .ToList();
    }

    private async Task<IReadOnlyList<EmailTarget>> ResolveDriverAsync(
        Guid? employeeId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (employeeId is not { } id)
        {
            return [];
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == id)
            .Select(e => new { e.Email, e.FirstName, e.LastName, e.PreferredLanguageCode })
            .FirstOrDefaultAsync(cancellationToken);

        return employee is null || string.IsNullOrWhiteSpace(employee.Email)
            ? []
            : [new EmailTarget(MessageOwnerType.Employee, id, employee.Email, $"{employee.FirstName} {employee.LastName}".Trim(),
                employee.PreferredLanguageCode, null)];
    }

    // --- shared helpers ---

    private async Task<string> ResolveLanguageAsync(
        string? recipientPreferred, Guid? customerId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(recipientPreferred))
        {
            return recipientPreferred;
        }

        if (customerId is { } id)
        {
            var customerLanguage = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == id)
                .Select(c => c.DefaultLanguageCode)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(customerLanguage))
            {
                return customerLanguage;
            }
        }

        var tenantLanguage = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.DefaultLanguage)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(tenantLanguage) ? "nl" : tenantLanguage;
    }

    private static List<RecipientSpec>? ParseRecipients(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<RecipientSpec>>(json);
            return parsed is { Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DefaultMessage(IReadOnlyDictionary<string, string> tokens)
    {
        var values = tokens.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Take(3).ToList();
        return values.Count == 0 ? "Er is een update beschikbaar." : string.Join(" — ", values);
    }
}
