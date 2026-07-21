using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Partners.Controllers;

[ApiController]
[Route("api/customers/{customerId:guid}/communication-rules")]
public class CustomerCommunicationController : ControllerBase
{
    private readonly ICustomerCommunicationService _service;

    public CustomerCommunicationController(ICustomerCommunicationService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<IReadOnlyList<CustomerCommunicationRuleDto>>> List(Guid customerId, CancellationToken cancellationToken)
    {
        var rules = await _service.ListAsync(customerId, cancellationToken);
        return rules is null ? NotFound() : Ok(rules);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CustomersManageCommunication)]
    public async Task<ActionResult<CustomerCommunicationRuleDto>> Create(
        Guid customerId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(customerId, request, cancellationToken);
        return created is null ? NotFound() : Ok(created);
    }

    [HttpPut("{ruleId:guid}")]
    [RequirePermission(PermissionCodes.CustomersManageCommunication)]
    public async Task<ActionResult<CustomerCommunicationRuleDto>> Update(
        Guid customerId, Guid ruleId, SaveCustomerCommunicationRuleRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(customerId, ruleId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{ruleId:guid}")]
    [RequirePermission(PermissionCodes.CustomersManageCommunication)]
    public async Task<IActionResult> Delete(Guid customerId, Guid ruleId, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(customerId, ruleId, cancellationToken) ? NoContent() : NotFound();
    }
}
