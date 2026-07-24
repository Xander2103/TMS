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

    public PricingController(IPricingAdminService admin, IPricingEngine engine)
    {
        _admin = admin;
        _engine = engine;
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

    // --- Price rules ---

    [HttpGet("api/pricing/rules")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<PriceRuleDto>>> ListRules([FromQuery] Guid? customerId, CancellationToken cancellationToken) =>
        Ok(await _admin.ListRulesAsync(customerId, cancellationToken));

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

    // --- Service options ---

    [HttpGet("api/service-options")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<ServiceOptionDto>>> ListServiceOptions(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await _admin.ListServiceOptionsAsync(includeInactive, cancellationToken));

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
}
