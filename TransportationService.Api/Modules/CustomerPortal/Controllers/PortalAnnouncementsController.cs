using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.CustomerPortal.Controllers;

/// <summary>Admin CRUD for customer-portal broadcast announcements.</summary>
[ApiController]
[Route("api/portal-announcements")]
[RequirePermission(PermissionCodes.PortalAnnouncementsManage)]
public class PortalAnnouncementsController : ControllerBase
{
    private readonly IPortalAnnouncementService _service;

    public PortalAnnouncementsController(IPortalAnnouncementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortalAnnouncementDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _service.ListAllAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PortalAnnouncementDto>> Create(
        SavePortalAnnouncementRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PortalAnnouncementDto>> Update(
        Guid id, SavePortalAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
