using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Messaging.Controllers;

/// <summary>
/// Admin surface for the notification-event catalog: per-tenant rule overrides (recipients,
/// channels, enable/disable) and per-customer opt-outs. The catalog itself is static code; only
/// the deviation from its defaults is ever persisted (an absent row means "use the catalog").
/// </summary>
[ApiController]
public class NotificationRulesController : ControllerBase
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public NotificationRulesController(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public record NotificationRuleDto(
        string EventKey, string Label, string Group, IReadOnlyList<string> AllowedTokens,
        bool Enabled, bool InAppEnabled, bool EmailEnabled, bool AllowCustomerOverride,
        IReadOnlyList<RecipientSpec> Recipients, bool IsCustomized, bool PeppolPending);

    [HttpGet("api/notification-rules")]
    [RequirePermission(PermissionCodes.NotificationRulesView)]
    public async Task<ActionResult<IReadOnlyList<NotificationRuleDto>>> List(CancellationToken cancellationToken)
    {
        var rules = await _dbContext.NotificationRules.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .ToDictionaryAsync(r => r.EventKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var result = NotificationEventCatalog.All
            .OrderBy(e => e.Group).ThenBy(e => e.Label)
            .Select(e =>
            {
                var rule = rules.GetValueOrDefault(e.EventKey);
                return new NotificationRuleDto(
                    e.EventKey, e.Label, e.Group, e.AllowedTokens,
                    rule?.Enabled ?? true, rule?.InAppEnabled ?? e.DefaultInApp, rule?.EmailEnabled ?? e.DefaultEmail,
                    rule?.AllowCustomerOverride ?? false,
                    ParseRecipients(rule?.RecipientsJson) ?? e.DefaultRecipients,
                    IsCustomized: rule is not null, e.PeppolPending);
            })
            .ToList();
        return Ok(result);
    }

    public record UpsertNotificationRuleRequest(
        bool Enabled, bool InAppEnabled, bool EmailEnabled, bool AllowCustomerOverride,
        IReadOnlyList<RecipientSpec> Recipients);

    [HttpPut("api/notification-rules/{eventKey}")]
    [RequirePermission(PermissionCodes.NotificationRulesManage)]
    public async Task<IActionResult> Upsert(string eventKey, UpsertNotificationRuleRequest request, CancellationToken cancellationToken)
    {
        if (NotificationEventCatalog.Resolve(eventKey) is null)
        {
            return NotFound(new { message = "Onbekende gebeurtenis." });
        }

        var rule = await _dbContext.NotificationRules
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.EventKey == eventKey, cancellationToken);
        var before = rule is null
            ? null
            : new { rule.Enabled, rule.InAppEnabled, rule.EmailEnabled, rule.AllowCustomerOverride, rule.RecipientsJson };
        var isNew = rule is null;
        if (rule is null)
        {
            rule = new NotificationRule { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, EventKey = eventKey };
            _dbContext.Add(rule);
        }

        rule.Enabled = request.Enabled;
        rule.InAppEnabled = request.InAppEnabled;
        rule.EmailEnabled = request.EmailEnabled;
        rule.AllowCustomerOverride = request.AllowCustomerOverride;
        rule.RecipientsJson = request.Recipients.Count == 0 ? null : JsonSerializer.Serialize(request.Recipients);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("NotificationRule", eventKey, isNew ? "Created" : "Updated", before,
            new { rule.Enabled, rule.InAppEnabled, rule.EmailEnabled, rule.AllowCustomerOverride, rule.RecipientsJson },
            cancellationToken);

        return NoContent();
    }

    public record CustomerNotificationOverrideDto(string EventKey, string Label, bool AllowCustomerOverride, bool? Enabled);

    [HttpGet("api/customers/{customerId:guid}/notification-overrides")]
    [RequirePermission(PermissionCodes.NotificationRulesView)]
    public async Task<ActionResult<IReadOnlyList<CustomerNotificationOverrideDto>>> ListForCustomer(
        Guid customerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return NotFound();
        }

        var tenantId = _tenantContext.TenantId;
        var rules = await _dbContext.NotificationRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToDictionaryAsync(r => r.EventKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var overrides = await _dbContext.CustomerNotificationOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.CustomerId == customerId)
            .ToDictionaryAsync(o => o.EventKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var result = NotificationEventCatalog.All
            .Select(e => new { Event = e, AllowOverride = rules.GetValueOrDefault(e.EventKey)?.AllowCustomerOverride ?? false })
            .Where(x => x.AllowOverride)
            .OrderBy(x => x.Event.Group).ThenBy(x => x.Event.Label)
            .Select(x => new CustomerNotificationOverrideDto(
                x.Event.EventKey, x.Event.Label, x.AllowOverride, overrides.GetValueOrDefault(x.Event.EventKey)?.Enabled))
            .ToList();
        return Ok(result);
    }

    public record SetCustomerNotificationOverrideRequest(bool? Enabled);

    [HttpPut("api/customers/{customerId:guid}/notification-overrides/{eventKey}")]
    [RequirePermission(PermissionCodes.NotificationRulesManage)]
    public async Task<IActionResult> SetForCustomer(
        Guid customerId, string eventKey, SetCustomerNotificationOverrideRequest request, CancellationToken cancellationToken)
    {
        if (NotificationEventCatalog.Resolve(eventKey) is null)
        {
            return NotFound(new { message = "Onbekende gebeurtenis." });
        }

        if (!await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return NotFound();
        }

        var tenantId = _tenantContext.TenantId;
        var overrideRow = await _dbContext.CustomerNotificationOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.CustomerId == customerId && o.EventKey == eventKey, cancellationToken);
        var before = overrideRow?.Enabled;
        if (overrideRow is null)
        {
            overrideRow = new CustomerNotificationOverride
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, EventKey = eventKey,
            };
            _dbContext.Add(overrideRow);
        }

        overrideRow.Enabled = request.Enabled;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("CustomerNotificationOverride", $"{customerId}:{eventKey}", "Set",
            new { Enabled = before }, new { overrideRow.Enabled }, cancellationToken);

        return NoContent();
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
}
