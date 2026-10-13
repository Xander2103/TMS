using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Services;

namespace TransportationService.Api.Modules.Locations.Controllers;

/// <summary>
/// The customer ↔ physical address relationship (sprint 2, central address master). Reuses the
/// existing <c>locations.*</c> permissions: deciding which addresses a customer uses is address
/// management, so no new permission codes (and no role-version bump) are introduced.
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}/addresses")]
public class CustomerAddressesController : ControllerBase
{
    private readonly ICustomerAddressService _service;

    public CustomerAddressesController(ICustomerAddressService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.LocationsView)]
    public async Task<ActionResult<IReadOnlyList<CustomerAddressDto>>> List(
        Guid customerId, [FromQuery] bool includeInactive, CancellationToken cancellationToken)
        => Ok(await _service.ListForCustomerAsync(customerId, includeInactive, cancellationToken));

    /// <summary>Links an EXISTING central address to this customer; never creates an address.</summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.LocationsEdit)]
    public async Task<ActionResult<CustomerAddressDto>> Link(
        Guid customerId, LinkCustomerAddressRequest request, CancellationToken cancellationToken)
        => Handle(await _service.LinkAsync(customerId, request, cancellationToken));

    [HttpPut("{linkId:guid}")]
    [RequirePermission(PermissionCodes.LocationsEdit)]
    public async Task<ActionResult<CustomerAddressDto>> Update(
        Guid customerId, Guid linkId, UpdateCustomerAddressLinkRequest request, CancellationToken cancellationToken)
        => Handle(await _service.UpdateLinkAsync(customerId, linkId, request, cancellationToken));

    /// <summary>Removes the relationship only — the physical address stays available to others.</summary>
    [HttpDelete("{linkId:guid}")]
    [RequirePermission(PermissionCodes.LocationsEdit)]
    public async Task<IActionResult> Unlink(Guid customerId, Guid linkId, CancellationToken cancellationToken)
        => await _service.UnlinkAsync(customerId, linkId, cancellationToken) ? NoContent() : NotFound();

    private ActionResult<CustomerAddressDto> Handle(CustomerAddressResult result) => result.Outcome switch
    {
        CustomerAddressOutcome.Success => Ok(result.Address!),
        CustomerAddressOutcome.NotFound => NotFound(),
        CustomerAddressOutcome.AlreadyLinked => Conflict(new ProblemDetails
        {
            Title = "Adres al gekoppeld",
            Detail = "Deze klant is al aan dit adres gekoppeld.",
            Status = StatusCodes.Status409Conflict,
        }),
        _ => BadRequest(new ProblemDetails
        {
            Title = "Onbekende verwijzing",
            Detail = "De klant of het adres bestaat niet.",
            Status = StatusCodes.Status400BadRequest,
        }),
    };
}

/// <summary>
/// Address-master lookups that are not scoped to one customer: duplicate detection before
/// creating an address, and the prioritised picker used by order/dossier stops.
/// </summary>
[ApiController]
[Route("api/addresses")]
public class AddressMasterController : ControllerBase
{
    private readonly ICustomerAddressService _service;

    public AddressMasterController(ICustomerAddressService service)
    {
        _service = service;
    }

    /// <summary>
    /// "Does this address already exist?" — matched on the normalised physical fields, not on a
    /// display string. An exact match must be overridden deliberately by the user.
    /// </summary>
    [HttpPost("duplicate-check")]
    [RequirePermission(PermissionCodes.LocationsView)]
    public async Task<ActionResult<AddressDuplicateCheckResultDto>> DuplicateCheck(
        AddressDuplicateCheckRequest request, CancellationToken cancellationToken)
        => Ok(await _service.CheckDuplicatesAsync(request, cancellationToken));

    /// <summary>Customer addresses first, then recently used, then the rest of the master.</summary>
    [HttpGet("picker")]
    [RequirePermission(PermissionCodes.LocationsView)]
    public async Task<ActionResult<IReadOnlyList<AddressPickerOptionDto>>> Picker(
        [FromQuery] Guid? customerId, [FromQuery] string? search, [FromQuery] int take,
        [FromQuery] Guid? excludeCustomerId, CancellationToken cancellationToken)
        => Ok(await _service.PickerAsync(customerId, search, take, excludeCustomerId, cancellationToken));
}
