using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private const int NameMaxLength = 150;

    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.RolesView)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _roleService.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.RolesView)]
    public async Task<ActionResult<RoleDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var role = await _roleService.GetByIdAsync(id, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.RolesCreate)]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            return BadRequest($"Name is required and must be at most {NameMaxLength} characters.");
        }

        try
        {
            var created = await _roleService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DbUpdateException)
        {
            return Conflict("A role with this name already exists.");
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.RolesEdit)]
    public async Task<ActionResult<RoleDto>> Update(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            return BadRequest($"Name is required and must be at most {NameMaxLength} characters.");
        }

        var result = await _roleService.UpdateAsync(id, request, cancellationToken);
        return HandleOperationResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCodes.RolesDelete)]
    public async Task<ActionResult<RoleDto>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _roleService.DeactivateAsync(id, cancellationToken);
        return HandleOperationResult(result);
    }

    [HttpPost("{id:guid}/permissions")]
    [RequirePermission(PermissionCodes.RolesManagePermissions)]
    public async Task<ActionResult<RoleDto>> AssignPermissions(Guid id, AssignPermissionsRequest request, CancellationToken cancellationToken)
    {
        var result = await _roleService.AssignPermissionsAsync(id, request, cancellationToken);
        return HandleOperationResult(result);
    }

    private ActionResult<RoleDto> HandleOperationResult(RoleOperationResult result)
    {
        return result.Outcome switch
        {
            RoleOperationOutcome.Success => Ok(result.Role),
            RoleOperationOutcome.NotFound => NotFound(),
            RoleOperationOutcome.SystemRoleProtected => Conflict("System roles cannot be renamed or deactivated."),
            _ => Conflict(),
        };
    }
}
