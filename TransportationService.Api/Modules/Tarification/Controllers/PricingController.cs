using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Services;

namespace TransportationService.Api.Modules.Tarification.Controllers;

/// <summary>Pricing configuration (zones, rules, service options, customer config) + preview.</summary>
[ApiController]
public class PricingController : ControllerBase
{
    private readonly IPricingAdminService _admin;
    private readonly IPricingEngine _engine;
    private readonly IPriceAdjustmentService _adjustments;

    public PricingController(IPricingAdminService admin, IPricingEngine engine, IPriceAdjustmentService adjustments)
    {
        _admin = admin;
        _engine = engine;
        _adjustments = adjustments;
    }

    // --- Preview (order entry) ---

    [HttpPost("api/pricing/preview")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.TariffsView)]
    public async Task<ActionResult<PriceCalculationResult>> Preview(PriceCalculationRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _engine.CalculateAsync(request, cancellationToken));
    }

    // --- Zones ---

    [HttpGet("api/pricing/zones")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage, PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<PricingZoneDto>>> ListZones(CancellationToken cancellationToken) =>
        Ok(await _admin.ListZonesAsync(cancellationToken));

    [HttpPost("api/pricing/zones")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingZoneDto>> CreateZone(SavePricingZoneRequest request, CancellationToken cancellationToken) =>
        Ok(await _admin.CreateZoneAsync(request, cancellationToken));

    [HttpPut("api/pricing/zones/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingZoneDto>> UpdateZone(Guid id, SavePricingZoneRequest request, CancellationToken cancellationToken)
    {
        var updated = await _admin.UpdateZoneAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/pricing/zones/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteZone(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteZoneAsync(id, cancellationToken) ? NoContent() : NotFound();

    // --- Tenant holidays (Wave 3 §4: drive Holiday time surcharges) ---

    [HttpGet("api/pricing/holidays")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<TenantHolidayDto>>> ListHolidays(CancellationToken cancellationToken) =>
        Ok(await _admin.ListHolidaysAsync(cancellationToken));

    [HttpPost("api/pricing/holidays")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<TenantHolidayDto>> CreateHoliday(SaveTenantHolidayRequest request, CancellationToken cancellationToken) =>
        Ok(await _admin.CreateHolidayAsync(request, cancellationToken));

    [HttpDelete("api/pricing/holidays/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteHoliday(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteHolidayAsync(id, cancellationToken) ? NoContent() : NotFound();

    // --- Pricing agreements (rate cards) ---

    [HttpGet("api/pricing/agreements")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PricingAgreementDto>>> ListAgreements(
        [FromQuery] Guid? customerId, [FromQuery] bool all, CancellationToken cancellationToken) =>
        Ok(await _admin.ListAgreementsAsync(customerId, cancellationToken, all));

    [HttpGet("api/pricing/agreements/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingAgreementDto>> GetAgreement(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await _admin.GetAgreementAsync(id, cancellationToken);
        return agreement is null ? NotFound() : Ok(agreement);
    }

    [HttpPost("api/pricing/agreements")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingAgreementDto>> CreateAgreement(
        SavePricingAgreementRequest request, CancellationToken cancellationToken) =>
        Ok(await _admin.CreateAgreementAsync(request, cancellationToken));

    [HttpPut("api/pricing/agreements/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingAgreementDto>> UpdateAgreement(
        Guid id, SavePricingAgreementRequest request, CancellationToken cancellationToken)
    {
        var updated = await _admin.UpdateAgreementAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/pricing/agreements/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteAgreement(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteAgreementAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>
    /// Duplicates an agreement as a new version: copies rules/brackets/surcharges/modifiers with a
    /// new effective window (optionally adjusted). Assignments are NOT copied — the new version
    /// must be linked to customers explicitly via the assignments endpoint.
    /// </summary>
    [HttpPost("api/pricing/agreements/{id:guid}/duplicate")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingAgreementDto>> DuplicateAgreement(
        Guid id, DuplicateAgreementRequest request, CancellationToken cancellationToken)
    {
        var duplicated = await _admin.DuplicateAgreementAsync(id, request, cancellationToken);
        return duplicated is null ? NotFound() : Ok(duplicated);
    }

    /// <summary>"Controle": configuration-health checks for one agreement (never blocks; every finding is reported).</summary>
    [HttpGet("api/pricing/agreements/{id:guid}/validate")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PricingConfigCheckDto>>> ValidateAgreementConfiguration(
        Guid id, CancellationToken cancellationToken)
    {
        var checks = await _admin.ValidateAgreementConfigurationAsync(id, cancellationToken);
        return checks is null ? NotFound() : Ok(checks);
    }

    // --- Pricing agreement assignments (shared tables → customers) ---

    [HttpGet("api/pricing/agreements/{id:guid}/assignments")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PricingAgreementAssignmentDto>>> ListAssignments(
        Guid id, CancellationToken cancellationToken)
    {
        var assignments = await _admin.ListAssignmentsAsync(id, cancellationToken);
        return assignments is null ? NotFound() : Ok(assignments);
    }

    [HttpPut("api/pricing/agreements/{id:guid}/assignments")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PricingAgreementAssignmentDto>>> SaveAssignments(
        Guid id, IReadOnlyList<SavePricingAssignmentRequest> request, CancellationToken cancellationToken)
    {
        var assignments = await _admin.SaveAssignmentsAsync(id, request, cancellationToken);
        return assignments is null ? NotFound() : Ok(assignments);
    }

    // --- Price rules ---

    [HttpGet("api/pricing/rules")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PriceRuleDto>>> ListRules(
        [FromQuery] Guid? customerId, [FromQuery] Guid? agreementId, CancellationToken cancellationToken) =>
        Ok(await _admin.ListRulesAsync(customerId, cancellationToken, agreementId));

    [HttpPost("api/pricing/rules")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PriceRuleDto>> CreateRule(SavePriceRuleRequest request, CancellationToken cancellationToken) =>
        Ok(await _admin.CreateRuleAsync(request, cancellationToken));

    [HttpPut("api/pricing/rules/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PriceRuleDto>> UpdateRule(Guid id, SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        var updated = await _admin.UpdateRuleAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/pricing/rules/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteRuleAsync(id, cancellationToken) ? NoContent() : NotFound();

    // --- Bracket-row customer overrides ("klantafwijkingen") ---

    [HttpGet("api/pricing/rules/{ruleId:guid}/bracket-overrides")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PriceRuleBracketOverrideDto>>> ListBracketOverrides(
        Guid ruleId, [FromQuery] Guid? customerId, CancellationToken cancellationToken)
    {
        var overrides = await _admin.ListBracketOverridesAsync(ruleId, customerId, cancellationToken);
        return overrides is null ? NotFound() : Ok(overrides);
    }

    [HttpPost("api/pricing/rules/{ruleId:guid}/bracket-overrides")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PriceRuleBracketOverrideDto>> CreateBracketOverride(
        Guid ruleId, SavePriceRuleBracketOverrideRequest request, CancellationToken cancellationToken)
    {
        var created = await _admin.CreateBracketOverrideAsync(ruleId, request, cancellationToken);
        return created is null ? NotFound() : Ok(created);
    }

    [HttpPut("api/pricing/bracket-overrides/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PriceRuleBracketOverrideDto>> UpdateBracketOverride(
        Guid id, SavePriceRuleBracketOverrideRequest request, CancellationToken cancellationToken)
    {
        var updated = await _admin.UpdateBracketOverrideAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/pricing/bracket-overrides/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteBracketOverride(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteBracketOverrideAsync(id, cancellationToken) ? NoContent() : NotFound();

    // --- Service options ---

    [HttpGet("api/service-options")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<ServiceOptionDto>>> ListServiceOptions(
        [FromQuery] bool includeInactive = false, [FromQuery] bool forOrderEntry = false,
        CancellationToken cancellationToken = default) =>
        Ok(await _admin.ListServiceOptionsAsync(includeInactive, forOrderEntry, cancellationToken));

    [HttpPost("api/service-options")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ServiceOptionDto>> CreateServiceOption(SaveServiceOptionRequest request, CancellationToken cancellationToken) =>
        Ok(await _admin.CreateServiceOptionAsync(request, cancellationToken));

    [HttpPut("api/service-options/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ServiceOptionDto>> UpdateServiceOption(Guid id, SaveServiceOptionRequest request, CancellationToken cancellationToken)
    {
        var updated = await _admin.UpdateServiceOptionAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/service-options/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteServiceOption(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteServiceOptionAsync(id, cancellationToken) ? NoContent() : NotFound();

    // --- Customer pricing configuration ---

    [HttpGet("api/customers/{customerId:guid}/pricing-config")]
    [RequirePermission(PermissionCodes.CustomersView, PermissionCodes.TariffsView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<CustomerPricingConfigDto>> GetCustomerConfig(Guid customerId, CancellationToken cancellationToken)
    {
        var config = await _admin.GetCustomerConfigAsync(customerId, cancellationToken);
        return config is null ? NotFound() : Ok(config);
    }

    [HttpPut("api/customers/{customerId:guid}/pricing-config")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<CustomerPricingConfigDto>> SaveCustomerConfig(
        Guid customerId, SaveCustomerPricingConfigRequest request, CancellationToken cancellationToken)
    {
        var config = await _admin.SaveCustomerConfigAsync(customerId, request, cancellationToken);
        return config is null ? NotFound() : Ok(config);
    }

    // --- Scheduled price adjustments ---

    [HttpGet("api/customers/{customerId:guid}/price-adjustments")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<ScheduledPriceAdjustmentDto>>> ListPriceAdjustments(
        Guid customerId, CancellationToken cancellationToken) =>
        Ok(await _adjustments.ListAsync(customerId, cancellationToken));

    [HttpPost("api/customers/{customerId:guid}/price-adjustments/preview")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PriceAdjustmentRulePreview>>> PreviewPriceAdjustment(
        Guid customerId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        Ok(await _adjustments.PreviewAsync(customerId, request, cancellationToken));

    [HttpPost("api/customers/{customerId:guid}/price-adjustments")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ScheduledPriceAdjustmentDto>> CreatePriceAdjustment(
        Guid customerId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        Ok(await _adjustments.CreateAsync(customerId, request, cancellationToken));

    [HttpPost("api/customers/{customerId:guid}/price-adjustments/{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ScheduledPriceAdjustmentDto>> CancelPriceAdjustment(
        Guid customerId, Guid id, CancellationToken cancellationToken)
    {
        var cancelled = await _adjustments.CancelAsync(customerId, id, cancellationToken);
        return cancelled is null ? NotFound() : Ok(cancelled);
    }

    // --- Scheduled price adjustments (agreement-scoped) ---

    [HttpGet("api/pricing/agreements/{id:guid}/price-adjustments")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<ScheduledPriceAdjustmentDto>>> ListAgreementPriceAdjustments(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await _adjustments.ListForAgreementAsync(id, cancellationToken));

    [HttpPost("api/pricing/agreements/{id:guid}/price-adjustments/preview")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PriceAdjustmentRulePreview>>> PreviewAgreementPriceAdjustment(
        Guid id, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        Ok(await _adjustments.PreviewForAgreementAsync(id, request, cancellationToken));

    [HttpPost("api/pricing/agreements/{id:guid}/price-adjustments")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ScheduledPriceAdjustmentDto>> CreateAgreementPriceAdjustment(
        Guid id, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        Ok(await _adjustments.CreateForAgreementAsync(id, request, cancellationToken));

    [HttpPost("api/pricing/agreements/{id:guid}/price-adjustments/{adjustmentId:guid}/cancel")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ScheduledPriceAdjustmentDto>> CancelAgreementPriceAdjustment(
        Guid id, Guid adjustmentId, CancellationToken cancellationToken)
    {
        var cancelled = await _adjustments.CancelForAgreementAsync(id, adjustmentId, cancellationToken);
        return cancelled is null ? NotFound() : Ok(cancelled);
    }

    // --- Combined-unit degression discounts (spec §29-31) ---

    [HttpGet("api/pricing/combined-discounts")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<CombinedUnitDiscountDto>>> ListCombinedDiscounts(
        [FromQuery] Guid? customerId, [FromQuery] Guid? agreementId, CancellationToken cancellationToken) =>
        Ok(await _admin.ListCombinedDiscountsAsync(customerId, agreementId, cancellationToken));

    [HttpPost("api/pricing/combined-discounts")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<CombinedUnitDiscountDto>> CreateCombinedDiscount(
        SaveCombinedUnitDiscountRequest request, CancellationToken cancellationToken) =>
        Ok(await _admin.CreateCombinedDiscountAsync(request, cancellationToken));

    [HttpPut("api/pricing/combined-discounts/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<CombinedUnitDiscountDto>> UpdateCombinedDiscount(
        Guid id, SaveCombinedUnitDiscountRequest request, CancellationToken cancellationToken)
    {
        var updated = await _admin.UpdateCombinedDiscountAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/pricing/combined-discounts/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> DeleteCombinedDiscount(Guid id, CancellationToken cancellationToken) =>
        await _admin.DeleteCombinedDiscountAsync(id, cancellationToken) ? NoContent() : NotFound();
}
