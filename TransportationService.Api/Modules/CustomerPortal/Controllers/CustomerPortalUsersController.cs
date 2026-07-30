using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.CustomerPortal.Controllers;

/// <summary>
/// Self-service management of a customer's OWN portal users. Exactly like every other endpoint
/// under api/customer-portal, the customer scope comes exclusively from the caller's own linked
/// customer (User.CustomerId) — never from a client-supplied id — and requires
/// customer_portal.manage_users on top of the caller being a portal user in the first place.
/// </summary>
[ApiController]
[Route("api/customer-portal/users")]
[RequirePermission(PermissionCodes.CustomerPortalManageUsers)]
public class CustomerPortalUsersController : ControllerBase
{
    private readonly ICustomerPortalUserService _service;

    public CustomerPortalUsersController(ICustomerPortalUserService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortalUserListItemDto>>> List(CancellationToken cancellationToken)
    {
        var result = await _service.ListAsync(cancellationToken);
        return result.Outcome switch
        {
            PortalOutcomeKind.Success => Ok(result.Value),
            PortalOutcomeKind.NoCustomerLink => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    [HttpPost]
    public async Task<ActionResult<PortalUserInviteResultDto>> Invite(
        PortalInviteUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.InviteAsync(request, cancellationToken);
        return Handle(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<PortalUserListItemDto>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        Handle(await _service.DeactivateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<PortalUserListItemDto>> Reactivate(Guid id, CancellationToken cancellationToken) =>
        Handle(await _service.ReactivateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/resend-invite")]
    public async Task<ActionResult<PortalUserInviteResultDto>> ResendInvite(Guid id, CancellationToken cancellationToken) =>
        Handle(await _service.ResendInviteAsync(id, cancellationToken));

    [HttpPut("{id:guid}/grants")]
    public async Task<ActionResult<PortalUserListItemDto>> SetGrants(
        Guid id, PortalUserGrantsRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.SetGrantsAsync(id, request.Grants, cancellationToken));

    private ActionResult<T> Handle<T>(PortalUserOperationResult<T> result) where T : class => result.Outcome switch
    {
        PortalUserOperationOutcome.Success => Ok(result.Value),
        PortalUserOperationOutcome.NotFound => NotFound(),
        _ => BadRequest(new { message = result.Error }),
    };
}
