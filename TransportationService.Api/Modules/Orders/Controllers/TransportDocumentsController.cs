using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>
/// Wave 9: delivery-note/CMR generation — per order and as one merged batch per trip
/// ("print everything for this trip", in route order). Follow-up wave P1-P3: the resolved
/// document strategy per order, the per-order override, tenant document rules, and the
/// end-of-day customer/date batch.
/// </summary>
[ApiController]
public class TransportDocumentsController : ControllerBase
{
    private readonly ITransportDocumentService _service;
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public TransportDocumentsController(
        ITransportDocumentService service, TransportationDbContext dbContext,
        ITenantContext tenantContext, IAuditService auditService)
    {
        _service = service;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    [HttpGet("api/orders/{id:guid}/documents/{kind}")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> ForOrder(Guid id, string kind, CancellationToken cancellationToken)
    {
        if (!IsKnownKind(kind))
        {
            return BadRequest();
        }

        var result = await _service.RenderAsync(id, kind, cancellationToken);
        return result is null ? NotFound() : File(result.Value.Content, "application/pdf", result.Value.FileName);
    }

    [HttpGet("api/trips/{id:guid}/documents/{kind}")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<IActionResult> ForTrip(Guid id, string kind, CancellationToken cancellationToken)
    {
        if (!IsKnownKind(kind))
        {
            return BadRequest();
        }

        var result = await _service.RenderTripBatchAsync(id, kind, cancellationToken);
        return result is null ? NotFound() : File(result.Value.Content, "application/pdf", result.Value.FileName);
    }

    [HttpGet("api/orders/{id:guid}/documents/strategy")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<OrderDocumentStrategyDto>> Strategy(Guid id, CancellationToken cancellationToken)
    {
        var strategy = await _service.GetStrategyAsync(id, cancellationToken);
        return strategy is null ? NotFound() : Ok(strategy);
    }

    public record SetDocumentPreferenceRequest(string? Preference);

    /// <summary>The manual override: null = inherit the customer strategy again.</summary>
    [HttpPut("api/orders/{id:guid}/documents/preference")]
    [RequirePermission(PermissionCodes.OrdersManage)]
    public async Task<ActionResult<OrderDocumentStrategyDto>> SetPreference(
        Guid id, SetDocumentPreferenceRequest request, CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(request.Preference) ? null : request.Preference.Trim();
        if (normalized is not (null or "Own" or "CustomerDocument" or "NoneRequired"))
        {
            throw new DomainValidationException("preference",
                "Ongeldige documentkeuze. Toegestaan: Own, CustomerDocument, NoneRequired of leeg.");
        }

        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var old = order.DocumentPreference;
        if (old != normalized)
        {
            order.DocumentPreference = normalized;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync("TransportOrder", order.Id.ToString(), "DocumentPreferenceChanged",
                new { Preference = old }, new { Preference = normalized }, cancellationToken);
        }

        var strategy = await _service.GetStrategyAsync(id, cancellationToken);
        return Ok(strategy);
    }

    [HttpGet("api/customers/{id:guid}/documents/preview")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<CustomerDayDocumentsPreviewDto>> CustomerPreview(
        Guid id, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var preview = await _service.PreviewCustomerDayAsync(id, date, cancellationToken);
        return preview is null ? NotFound() : Ok(preview);
    }

    [HttpGet("api/customers/{id:guid}/documents/{kind}")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> CustomerBatch(
        Guid id, string kind, [FromQuery] DateOnly date, [FromQuery] string? orderIds,
        CancellationToken cancellationToken)
    {
        if (!IsKnownKind(kind))
        {
            return BadRequest();
        }

        var selection = string.IsNullOrWhiteSpace(orderIds)
            ? null
            : orderIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
        var result = await _service.RenderCustomerDayBatchAsync(id, date, kind, selection, cancellationToken);
        return result is null ? NotFound() : File(result.Value.Content, "application/pdf", result.Value.FileName);
    }

    // --- Tenant document rules (P2) ---

    public record DocumentRuleInput(
        int Priority, bool? MatchCrossBorder, bool? MatchAdr, Guid? MatchActivityTypeId, string DocumentKind);

    public record DocumentRuleDto(
        Guid Id, int Priority, bool? MatchCrossBorder, bool? MatchAdr, Guid? MatchActivityTypeId, string DocumentKind);

    [HttpGet("api/settings/document-rules")]
    [RequirePermission(PermissionCodes.CompanySettingsView, PermissionCodes.CompanySettingsManage)]
    public async Task<ActionResult<IReadOnlyList<DocumentRuleDto>>> Rules(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var rules = await _dbContext.TenantDocumentRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Priority)
            .Select(r => new DocumentRuleDto(
                r.Id, r.Priority, r.MatchCrossBorder, r.MatchAdr, r.MatchActivityTypeId, r.DocumentKind))
            .ToListAsync(cancellationToken);
        return Ok(rules);
    }

    /// <summary>Replace-all save, mirroring other settings surfaces. Empty list = built-in defaults.</summary>
    [HttpPut("api/settings/document-rules")]
    [RequirePermission(PermissionCodes.CompanySettingsManage)]
    public async Task<ActionResult<IReadOnlyList<DocumentRuleDto>>> SaveRules(
        IReadOnlyList<DocumentRuleInput> request, CancellationToken cancellationToken)
    {
        foreach (var input in request)
        {
            if (input.DocumentKind is not ("DeliveryNote" or "Cmr" or "None"))
            {
                throw new DomainValidationException("documentKind",
                    "Ongeldige documentsoort. Toegestaan: DeliveryNote, Cmr of None.");
            }
        }

        var tenantId = _tenantContext.TenantId;
        var existing = await _dbContext.TenantDocumentRules
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        foreach (var rule in existing)
        {
            rule.IsDeleted = true;
        }

        foreach (var input in request)
        {
            _dbContext.TenantDocumentRules.Add(new TenantDocumentRule
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                Priority = input.Priority, MatchCrossBorder = input.MatchCrossBorder,
                MatchAdr = input.MatchAdr, MatchActivityTypeId = input.MatchActivityTypeId,
                DocumentKind = input.DocumentKind,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TenantDocumentRules", tenantId.ToString(), "RulesReplaced",
            new { Count = existing.Count }, new { Count = request.Count }, cancellationToken);
        return await Rules(cancellationToken);
    }

    private static bool IsKnownKind(string kind) =>
        string.Equals(kind, "cmr", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "delivery-note", StringComparison.OrdinalIgnoreCase);
}
