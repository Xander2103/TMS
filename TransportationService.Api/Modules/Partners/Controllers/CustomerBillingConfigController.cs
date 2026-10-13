using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Partners.Controllers;

/// <summary>Diesel-surcharge config + PO policy/history of a customer.</summary>
[ApiController]
[Route("api/customers/{customerId:guid}")]
public class CustomerBillingConfigController : ControllerBase
{
    private readonly ICustomerBillingConfigService _service;

    public CustomerBillingConfigController(ICustomerBillingConfigService service)
    {
        _service = service;
    }

    [HttpGet("diesel-surcharge")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<CustomerDieselSurchargeDto>> GetSurcharge(Guid customerId, CancellationToken cancellationToken)
    {
        var config = await _service.GetSurchargeAsync(customerId, cancellationToken);
        return config is null ? NotFound() : Ok(config);
    }

    [HttpPut("diesel-surcharge")]
    [RequirePermission(PermissionCodes.CustomersManageSurcharge)]
    public async Task<ActionResult<CustomerDieselSurchargeDto>> SaveSurcharge(
        Guid customerId, SaveCustomerDieselSurchargeRequest request, CancellationToken cancellationToken)
    {
        var config = await _service.SaveSurchargeAsync(customerId, request, cancellationToken);
        return config is null ? NotFound() : Ok(config);
    }

    [HttpGet("po-policy")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<CustomerPoPolicyDto>> GetPoPolicy(Guid customerId, CancellationToken cancellationToken)
    {
        var policy = await _service.GetPoPolicyAsync(customerId, cancellationToken);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPut("po-policy")]
    [RequirePermission(PermissionCodes.CustomersManagePo)]
    public async Task<ActionResult<CustomerPoPolicyDto>> SetPoPolicy(
        Guid customerId, SetPurchaseOrderPolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _service.SetPoPolicyAsync(customerId, request, cancellationToken);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPost("po-numbers")]
    [RequirePermission(PermissionCodes.CustomersManagePo)]
    public async Task<ActionResult<CustomerPoPolicyDto>> AddPoNumber(
        Guid customerId, SaveCustomerPoNumberRequest request, CancellationToken cancellationToken)
    {
        var policy = await _service.AddPoNumberAsync(customerId, request, cancellationToken);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPut("po-numbers/{poId:guid}")]
    [RequirePermission(PermissionCodes.CustomersManagePo)]
    public async Task<ActionResult<CustomerPoPolicyDto>> UpdatePoNumber(
        Guid customerId, Guid poId, SaveCustomerPoNumberRequest request, CancellationToken cancellationToken)
    {
        var policy = await _service.UpdatePoNumberAsync(customerId, poId, request, cancellationToken);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpDelete("po-numbers/{poId:guid}")]
    [RequirePermission(PermissionCodes.CustomersManagePo)]
    public async Task<IActionResult> DeletePoNumber(Guid customerId, Guid poId, CancellationToken cancellationToken)
    {
        return await _service.DeletePoNumberAsync(customerId, poId, cancellationToken) ? NoContent() : NotFound();
    }
}
