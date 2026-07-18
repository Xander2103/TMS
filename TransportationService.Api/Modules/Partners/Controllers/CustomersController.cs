using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Partners.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? categoryId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _customerService.SearchAsync(search, isActive, categoryId, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<CustomerDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CustomersCreate)]
    public async Task<ActionResult<CustomerDetailDto>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Naam is verplicht." });
        }

        var created = await _customerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<ActionResult<CustomerDetailDto>> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Naam is verplicht." });
        }

        var updated = await _customerService.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.CustomersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _customerService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/blocked")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> SetBlocked(Guid id, SetCustomerBlockedRequest request, CancellationToken cancellationToken)
    {
        return await _customerService.SetBlockedAsync(id, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/contacts")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<ActionResult<CustomerContactDto>> AddContact(Guid id, CreateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "Voor- en achternaam zijn verplicht." });
        }

        var contact = await _customerService.AddContactAsync(id, request, cancellationToken);
        return contact is null ? NotFound() : Ok(contact);
    }

    [HttpPut("{id:guid}/contacts/{contactId:guid}")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<ActionResult<CustomerContactDto>> UpdateContact(Guid id, Guid contactId, UpdateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "Voor- en achternaam zijn verplicht." });
        }

        var contact = await _customerService.UpdateContactAsync(id, contactId, request, cancellationToken);
        return contact is null ? NotFound() : Ok(contact);
    }

    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> RemoveContact(Guid id, Guid contactId, CancellationToken cancellationToken)
    {
        return await _customerService.RemoveContactAsync(id, contactId, cancellationToken) ? NoContent() : NotFound();
    }
}
