using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Controllers;

[ApiController]
[Route("api/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly IRoleService _roleService;

    public PermissionsController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.RolesView)]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _roleService.ListPermissionsAsync(cancellationToken));
    }
}
