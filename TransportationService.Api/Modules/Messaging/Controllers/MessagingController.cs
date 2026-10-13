using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Messaging.Controllers;

/// <summary>
/// Outbox inspection/retry, per-owner messaging profiles, template management and preview.
/// Sending itself is fully server-driven (producers + dispatcher); nothing here sends directly.
/// </summary>
[ApiController]
public class MessagingController : ControllerBase
{
    /// <summary>Tokens every rendered message may use regardless of the event's own allowlist.</summary>
    private static readonly string[] GlobalTokens = ["companyName"];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public MessagingController(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public record OutboxRowDto(
        Guid Id, MessageChannel Channel, string Kind, string RecipientAddress, string? RecipientName,
        string? Subject, OutboxStatus Status, int AttemptCount, DateTime? NextAttemptAt, DateTime? SentAt,
        string? FailureReason, DateTime CreatedAt, bool IsFallback,
        string? RelatedEntityType, string? RelatedEntityId);

    /// <summary><paramref name="search"/> matches recipient address/name (admin "Verzonden"/"Mislukte
    /// berichten" tabs, Phase 7); <paramref name="channel"/> narrows by channel.</summary>
    [HttpGet("api/messaging/outbox")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<ActionResult<PagedResult<OutboxRowDto>>> Outbox(
        [FromQuery] OutboxStatus? status, [FromQuery] string? kind, [FromQuery] MessageChannel? channel,
        [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var pageRequest = PageRequest.Of(page, pageSize);
        var query = _dbContext.OutboxMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId);
        if (status is { } s) query = query.Where(m => m.Status == s);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(m => m.Kind == kind);
        if (channel is { } c) query = query.Where(m => m.Channel == c);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m => m.RecipientAddress.ToLower().Contains(term)
                                     || (m.RecipientName != null && m.RecipientName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .Select(m => new OutboxRowDto(
                m.Id, m.Channel, m.Kind, m.RecipientAddress, m.RecipientName, m.Subject,
                m.Status, m.AttemptCount, m.NextAttemptAt, m.SentAt, m.FailureReason, m.CreatedAt,
                m.FallbackOfMessageId != null, m.RelatedEntityType, m.RelatedEntityId))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<OutboxRowDto>(items, totalCount, pageRequest.Page, pageRequest.PageSize));
    }

    [HttpGet("api/messaging/outbox/{id:guid}")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<IActionResult> OutboxDetail(Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        return message is null ? NotFound() : Ok(message);
    }

    /// <summary>Failed messages go back to Pending with a fresh attempt budget.</summary>
    [HttpPost("api/messaging/outbox/{id:guid}/retry")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        if (message.Status is not (OutboxStatus.Failed or OutboxStatus.Suppressed))
        {
            return BadRequest(new { message = "Alleen mislukte of onderdrukte berichten kunnen opnieuw." });
        }

        message.Status = OutboxStatus.Pending;
        message.AttemptCount = 0;
        message.NextAttemptAt = null;
        message.FailureReason = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>P9: a dispatcher approves a review-held message; it joins the normal send queue.</summary>
    [HttpPost("api/messaging/outbox/{id:guid}/release")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        if (message.Status != OutboxStatus.AwaitingReview)
        {
            return BadRequest(new { message = "Alleen berichten in controle kunnen worden vrijgegeven." });
        }

        message.Status = OutboxStatus.Pending;
        message.NextAttemptAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OutboxMessage", message.Id.ToString(), "ReviewReleased",
            null, new { message.Kind, message.RecipientAddress }, cancellationToken);
        return NoContent();
    }

    public record RejectOutboxRequest(string? Reason);

    /// <summary>P9: a dispatcher rejects a review-held message; it is suppressed with the reason.</summary>
    [HttpPost("api/messaging/outbox/{id:guid}/reject")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<IActionResult> Reject(Guid id, RejectOutboxRequest request, CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        if (message.Status != OutboxStatus.AwaitingReview)
        {
            return BadRequest(new { message = "Alleen berichten in controle kunnen worden afgewezen." });
        }

        message.Status = OutboxStatus.Suppressed;
        message.FailureReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Afgewezen na controle door dispatch."
            : $"Afgewezen na controle: {request.Reason.Trim()}";
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OutboxMessage", message.Id.ToString(), "ReviewRejected",
            null, new { message.Kind, message.RecipientAddress, request.Reason }, cancellationToken);
        return NoContent();
    }

    public record MessagingProfileDto(
        MessageOwnerType OwnerType, Guid OwnerId, bool EmailEnabled, bool SmsEnabled,
        string? EmailAddress, string? PhoneNumber, string? EnabledKindsJson,
        string? PreferredLanguage, TimeOnly? QuietHoursFrom, TimeOnly? QuietHoursTo,
        MessageChannel? FallbackChannel);

    [HttpGet("api/messaging/profiles/{ownerType}/{ownerId:guid}")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<ActionResult<MessagingProfileDto>> GetProfile(
        MessageOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.MessagingProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId
                                      && p.OwnerType == ownerType && p.OwnerId == ownerId, cancellationToken);
        return Ok(profile is null
            ? new MessagingProfileDto(ownerType, ownerId, true, false, null, null, null, null, null, null, null)
            : new MessagingProfileDto(
                ownerType, ownerId, profile.EmailEnabled, profile.SmsEnabled, profile.EmailAddress,
                profile.PhoneNumber, profile.EnabledKindsJson, profile.PreferredLanguage,
                profile.QuietHoursFrom, profile.QuietHoursTo, profile.FallbackChannel));
    }

    public record UpsertProfileRequest(
        bool EmailEnabled, bool SmsEnabled, string? EmailAddress, string? PhoneNumber,
        string? EnabledKindsJson, string? PreferredLanguage,
        TimeOnly? QuietHoursFrom, TimeOnly? QuietHoursTo, MessageChannel? FallbackChannel);

    [HttpPut("api/messaging/profiles/{ownerType}/{ownerId:guid}")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<IActionResult> UpsertProfile(
        MessageOwnerType ownerType, Guid ownerId, UpsertProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.MessagingProfiles
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId
                                      && p.OwnerType == ownerType && p.OwnerId == ownerId, cancellationToken);
        if (profile is null)
        {
            profile = new MessagingProfile
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                OwnerType = ownerType,
                OwnerId = ownerId,
            };
            _dbContext.Add(profile);
        }

        profile.EmailEnabled = request.EmailEnabled;
        profile.SmsEnabled = request.SmsEnabled;
        profile.EmailAddress = request.EmailAddress;
        profile.PhoneNumber = request.PhoneNumber;
        profile.EnabledKindsJson = request.EnabledKindsJson;
        profile.PreferredLanguage = request.PreferredLanguage;
        profile.QuietHoursFrom = request.QuietHoursFrom;
        profile.QuietHoursTo = request.QuietHoursTo;
        profile.FallbackChannel = request.FallbackChannel;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public record TemplateDto(
        Guid Id, string Kind, MessageChannel Channel, string Language, Guid? CustomerId,
        string? Subject, string Body, string? BodyHtml, bool IsActive);

    [HttpGet("api/message-templates")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<ActionResult<IReadOnlyList<TemplateDto>>> Templates(CancellationToken cancellationToken)
    {
        var templates = await _dbContext.MessageTemplates.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.CustomerId == null)
            .OrderBy(t => t.Kind).ThenBy(t => t.Channel)
            .Select(t => new TemplateDto(t.Id, t.Kind, t.Channel, t.Language, t.CustomerId, t.Subject, t.Body, t.BodyHtml, t.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpGet("api/message-templates/kinds")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public ActionResult<IReadOnlyList<string>> Kinds() => Ok(MessageKinds.All);

    /// <summary>Placeholder tokens available for a template's chosen kind (admin editor, Phase 7):
    /// the catalog event's own tokens (kind and event key are identical for every catalog-linked
    /// kind, see NotificationEventCatalog) plus the tokens every template may use. Legacy
    /// pre-catalog kinds — and an omitted/unknown eventKey — resolve to just the global tokens,
    /// matching <see cref="ValidatePlaceholders"/>'s "unvalidated" treatment of those kinds.</summary>
    [HttpGet("api/message-templates/placeholders")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public ActionResult<IReadOnlyList<string>> Placeholders([FromQuery] string? eventKey)
    {
        var eventInfo = string.IsNullOrWhiteSpace(eventKey) ? null : NotificationEventCatalog.Resolve(eventKey);
        var tokens = new List<string>();
        if (eventInfo is not null) tokens.AddRange(eventInfo.AllowedTokens);
        tokens.AddRange(GlobalTokens);
        return Ok(tokens.Distinct().ToList());
    }

    public record CustomerTemplateDto(
        string Kind, MessageChannel Channel, string Language, bool IsOverridden,
        Guid? Id, string? Subject, string Body, string? BodyHtml, bool IsActive);

    /// <summary>Effective templates for one customer: every tenant-default row, each flagged
    /// whether this customer has an override (and if so, that override's content).</summary>
    [HttpGet("api/customers/{customerId:guid}/message-templates")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<ActionResult<IReadOnlyList<CustomerTemplateDto>>> CustomerTemplates(
        Guid customerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return NotFound();
        }

        var tenantId = _tenantContext.TenantId;
        var tenantDefaults = await _dbContext.MessageTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CustomerId == null)
            .ToListAsync(cancellationToken);
        var overrides = await _dbContext.MessageTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CustomerId == customerId)
            .ToDictionaryAsync(t => (t.Kind, t.Channel, t.Language), cancellationToken);

        var result = tenantDefaults
            .Select(d =>
            {
                var effective = overrides.GetValueOrDefault((d.Kind, d.Channel, d.Language));
                return effective is not null
                    ? new CustomerTemplateDto(d.Kind, d.Channel, d.Language, IsOverridden: true,
                        effective.Id, effective.Subject, effective.Body, effective.BodyHtml, effective.IsActive)
                    : new CustomerTemplateDto(d.Kind, d.Channel, d.Language, IsOverridden: false,
                        null, d.Subject, d.Body, d.BodyHtml, d.IsActive);
            })
            .OrderBy(t => t.Kind).ThenBy(t => t.Channel)
            .ToList();
        return Ok(result);
    }

    public record UpsertTemplateRequest(
        string Kind, MessageChannel Channel, string Language, string? Subject, string Body,
        string? BodyHtml, bool IsActive, Guid? CustomerId = null);

    [HttpPost("api/message-templates")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<ActionResult<TemplateDto>> UpsertTemplate(UpsertTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!MessageKinds.All.Contains(request.Kind) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { message = "Kies een geldig berichttype en een niet-lege inhoud." });
        }

        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return BadRequest(new { message = "De gekoppelde klant bestaat niet." });
        }

        ValidatePlaceholders(request.Kind, request.Subject, request.Body, request.BodyHtml);

        var sanitizedBodyHtml = string.IsNullOrWhiteSpace(request.BodyHtml) ? null : HtmlSanitizer.Sanitize(request.BodyHtml);

        var template = await _dbContext.MessageTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Kind == request.Kind
                                      && t.Channel == request.Channel && t.Language == request.Language
                                      && t.CustomerId == request.CustomerId, cancellationToken);
        var before = template is null ? null : new { template.Subject, template.Body, template.BodyHtml };
        var isNew = template is null;
        if (template is null)
        {
            template = new MessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                Kind = request.Kind,
                Channel = request.Channel,
                Language = request.Language,
                CustomerId = request.CustomerId,
            };
            _dbContext.Add(template);
        }

        template.Subject = request.Subject;
        template.Body = request.Body;
        template.BodyHtml = sanitizedBodyHtml;
        template.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("MessageTemplate", template.Id.ToString(), isNew ? "Created" : "Updated", before,
            new { template.Subject, template.Body, template.BodyHtml }, cancellationToken);

        return Ok(new TemplateDto(
            template.Id, template.Kind, template.Channel, template.Language, template.CustomerId,
            template.Subject, template.Body, template.BodyHtml, template.IsActive));
    }

    [HttpDelete("api/message-templates/{id:guid}")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        var template = await _dbContext.MessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        _dbContext.Remove(template); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("MessageTemplate", template.Id.ToString(), "Deleted",
            new { template.Subject, template.Body, template.BodyHtml }, null, cancellationToken);

        return NoContent();
    }

    /// <summary>Placeholders outside the event's allowed tokens (∪ global tokens) are rejected.
    /// Kinds with no catalog entry (legacy pre-Phase-6 kinds) are not validated — they predate
    /// the event catalog and keep their historical free-form tokens.</summary>
    private static void ValidatePlaceholders(string kind, string? subject, string body, string? bodyHtml)
    {
        var eventInfo = NotificationEventCatalog.All.FirstOrDefault(e => e.MessageKind == kind);
        if (eventInfo is null)
        {
            return;
        }

        var allowed = new HashSet<string>(eventInfo.AllowedTokens, StringComparer.OrdinalIgnoreCase);
        allowed.UnionWith(GlobalTokens);

        var used = MessageTemplateRenderer.ExtractTokens(subject)
            .Concat(MessageTemplateRenderer.ExtractTokens(body))
            .Concat(MessageTemplateRenderer.ExtractTokens(bodyHtml))
            .Distinct();
        foreach (var token in used)
        {
            if (!allowed.Contains(token))
            {
                throw new DomainValidationException("body", $"Onbekende placeholder {{{{{token}}}}}");
            }
        }
    }

    public record PreviewRequest(string Kind, MessageChannel Channel, string Language, Dictionary<string, string>? Tokens);

    public record PreviewResponse(string? Subject, string Body);

    /// <summary>Renders template + tokens without queueing anything.</summary>
    [HttpPost("api/message-templates/preview")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<ActionResult<PreviewResponse>> Preview(PreviewRequest request, CancellationToken cancellationToken)
    {
        var tokens = request.Tokens ?? new Dictionary<string, string>
        {
            ["orderNumber"] = "ORD-0042",
            ["customerName"] = "Voorbeeldklant",
            ["employeeName"] = "Jan Jansen",
            ["companyName"] = "Voorbeeld Transport",
            ["window"] = "08:00–12:00",
            ["eta"] = "14:30",
            ["reason"] = "file",
            ["period"] = "3 t/m 7 augustus",
            ["note"] = "",
            ["details"] = "voorbeeldinhoud",
            ["qualification"] = "Code 95",
            ["expiryDate"] = "01-09-2026",
        };

        var custom = await _dbContext.MessageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Kind == request.Kind
                                      && t.Channel == request.Channel && t.Language == request.Language && t.IsActive,
                cancellationToken);

        string? subjectTemplate;
        string bodyTemplate;
        if (custom is not null)
        {
            subjectTemplate = custom.Subject;
            bodyTemplate = custom.Body;
        }
        else
        {
            var builtIn = BuiltInMessageTemplates.Resolve(request.Kind, request.Channel);
            subjectTemplate = builtIn.Subject;
            bodyTemplate = builtIn.Body;
        }

        return Ok(new PreviewResponse(
            subjectTemplate is null ? null : MessageTemplateRenderer.Render(subjectTemplate, tokens),
            MessageTemplateRenderer.Render(bodyTemplate, tokens)));
    }
}
