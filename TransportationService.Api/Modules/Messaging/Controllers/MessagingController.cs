using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
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
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public MessagingController(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public record OutboxRowDto(
        Guid Id, MessageChannel Channel, string Kind, string RecipientAddress, string? RecipientName,
        string? Subject, OutboxStatus Status, int AttemptCount, DateTime? NextAttemptAt, DateTime? SentAt,
        string? FailureReason, DateTime CreatedAt, bool IsFallback);

    [HttpGet("api/messaging/outbox")]
    [RequirePermission(PermissionCodes.MessagingManage)]
    public async Task<ActionResult<PagedResult<OutboxRowDto>>> Outbox(
        [FromQuery] OutboxStatus? status, [FromQuery] string? kind,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var pageRequest = PageRequest.Of(page, pageSize);
        var query = _dbContext.OutboxMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId);
        if (status is { } s) query = query.Where(m => m.Status == s);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(m => m.Kind == kind);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .Select(m => new OutboxRowDto(
                m.Id, m.Channel, m.Kind, m.RecipientAddress, m.RecipientName, m.Subject,
                m.Status, m.AttemptCount, m.NextAttemptAt, m.SentAt, m.FailureReason, m.CreatedAt,
                m.FallbackOfMessageId != null))
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

    public record TemplateDto(Guid Id, string Kind, MessageChannel Channel, string Language, string? Subject, string Body, bool IsActive);

    [HttpGet("api/message-templates")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<ActionResult<IReadOnlyList<TemplateDto>>> Templates(CancellationToken cancellationToken)
    {
        var templates = await _dbContext.MessageTemplates.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId)
            .OrderBy(t => t.Kind).ThenBy(t => t.Channel)
            .Select(t => new TemplateDto(t.Id, t.Kind, t.Channel, t.Language, t.Subject, t.Body, t.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpGet("api/message-templates/kinds")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public ActionResult<IReadOnlyList<string>> Kinds() => Ok(MessageKinds.All);

    public record UpsertTemplateRequest(string Kind, MessageChannel Channel, string Language, string? Subject, string Body, bool IsActive);

    [HttpPost("api/message-templates")]
    [RequirePermission(PermissionCodes.MessageTemplatesManage)]
    public async Task<ActionResult<TemplateDto>> UpsertTemplate(UpsertTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!MessageKinds.All.Contains(request.Kind) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { message = "Kies een geldig berichttype en een niet-lege inhoud." });
        }

        var template = await _dbContext.MessageTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Kind == request.Kind
                                      && t.Channel == request.Channel && t.Language == request.Language, cancellationToken);
        if (template is null)
        {
            template = new MessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                Kind = request.Kind,
                Channel = request.Channel,
                Language = request.Language,
            };
            _dbContext.Add(template);
        }

        template.Subject = request.Subject;
        template.Body = request.Body;
        template.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new TemplateDto(template.Id, template.Kind, template.Channel, template.Language, template.Subject, template.Body, template.IsActive));
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
        return NoContent();
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
