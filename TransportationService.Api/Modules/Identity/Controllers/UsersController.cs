using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private const int EmailMaxLength = 250;
    private const int NameMaxLength = 100;

    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.UsersView)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _userService.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.UsersView)]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.UsersCreate)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        if (string.IsNullOrWhiteSpace(email) || email.Length > EmailMaxLength || !email.Contains('@'))
        {
            return BadRequest($"Email is required, must contain '@', and must be at most {EmailMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(firstName) || firstName.Length > NameMaxLength)
        {
            return BadRequest($"FirstName is required and must be at most {NameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(lastName) || lastName.Length > NameMaxLength)
        {
            return BadRequest($"LastName is required and must be at most {NameMaxLength} characters.");
        }

        try
        {
            var created = await _userService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DbUpdateException)
        {
            return Conflict("A user with this email already exists.");
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.UsersEdit)]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        if (string.IsNullOrWhiteSpace(firstName) || firstName.Length > NameMaxLength)
        {
            return BadRequest($"FirstName is required and must be at most {NameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(lastName) || lastName.Length > NameMaxLength)
        {
            return BadRequest($"LastName is required and must be at most {NameMaxLength} characters.");
        }

        var updated = await _userService.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    public record SetActiveRequest(bool IsActive);

    [HttpPatch("{id:guid}/active")]
    [RequirePermission(PermissionCodes.UsersEdit)]
    public async Task<ActionResult<UserDto>> SetActive(Guid id, SetActiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.SetActiveAsync(id, request.IsActive, cancellationToken);
        return HandleOperationResult(result);
    }

    public record SetBlockedRequest(bool IsBlocked);

    [HttpPatch("{id:guid}/blocked")]
    [RequirePermission(PermissionCodes.UsersBlock)]
    public async Task<ActionResult<UserDto>> SetBlocked(Guid id, SetBlockedRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.SetBlockedAsync(id, request.IsBlocked, cancellationToken);
        return HandleOperationResult(result);
    }

    [HttpPost("{id:guid}/roles")]
    [RequirePermission(PermissionCodes.UsersEdit)]
    public async Task<ActionResult<UserDto>> AssignRoles(Guid id, AssignRolesRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.AssignRolesAsync(id, request, cancellationToken);
        return HandleOperationResult(result);
    }

    private ActionResult<UserDto> HandleOperationResult(UserOperationResult result)
    {
        return result.Outcome switch
        {
            UserOperationOutcome.Success => Ok(result.User),
            UserOperationOutcome.NotFound => NotFound(),
            UserOperationOutcome.LastActiveAdministrator => Conflict("The last active administrator cannot be deactivated, blocked, or lose the administrator role."),
            _ => Conflict(),
        };
    }
}
