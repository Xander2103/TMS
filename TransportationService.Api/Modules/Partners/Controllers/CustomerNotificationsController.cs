using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Partners.Controllers;

public record SetContactSubscriptionsRequest(IReadOnlyList<string> OptionKeys);

/// <summary>
/// Sprint 3 — the contact-centric answer to "who receives what?". Writes to the same
/// communication rules as <see cref="CustomerCommunicationController"/>; that controller stays
/// the advanced surface for CC addresses, fallback contacts and language overrides.
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}")]
public class CustomerNotificationsController : ControllerBase
{
    private readonly ICustomerContactSubscriptionService _service;

    public CustomerNotificationsController(ICustomerContactSubscriptionService service)
    {
        _service = service;
    }

    /// <summary>The catalogue itself — stable keys and their business group, for the contact form.</summary>
    [HttpGet("/api/customer-notification-options")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public ActionResult<IReadOnlyList<object>> Catalog() =>
        Ok(CustomerNotificationCatalog.Options.Select(o => new { o.Key, Group = o.Group.ToString() }).ToList());

    [HttpGet("contacts/{contactId:guid}/notifications")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<ContactSubscriptionsDto>> Get(
        Guid customerId, Guid contactId, CancellationToken cancellationToken)
    {
        var result = await _service.GetForContactAsync(customerId, contactId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("contacts/{contactId:guid}/notifications")]
    [RequirePermission(PermissionCodes.CustomersManageCommunication)]
    public async Task<ActionResult<ContactSubscriptionsDto>> Set(
        Guid customerId, Guid contactId, SetContactSubscriptionsRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.SetForContactAsync(customerId, contactId, request.OptionKeys ?? [], cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>"Planning → Jan, Sofie" — the readable overview per notification type.</summary>
    [HttpGet("notification-overview")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<IReadOnlyList<NotificationOverviewLineDto>>> Overview(
        Guid customerId, CancellationToken cancellationToken)
    {
        var result = await _service.GetOverviewAsync(customerId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
