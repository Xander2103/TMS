using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// Reusable REST surface for a lookup entity: paged search, dropdown options, get-by-id,
/// create, update and (soft) delete. Concrete controllers only declare their route and the
/// two permission codes (view / manage). Permission checks are performed here rather than via
/// attributes because the required code varies per concrete controller.
/// </summary>
[ApiController]
public abstract class LookupControllerBase<TEntity> : ControllerBase where TEntity : LookupEntity, new()
{
    private readonly ILookupService<TEntity> _service;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    protected LookupControllerBase(
        ILookupService<TEntity> service,
        ICurrentUserContext currentUser,
        IPermissionAuthorizationService authorization)
    {
        _service = service;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    /// <summary>Permission required to read this lookup (list, options, detail).</summary>
    protected abstract string ViewPermission { get; }

    /// <summary>Permission required to create, update or delete this lookup.</summary>
    protected abstract string ManagePermission { get; }

    [HttpGet]
    public async Task<ActionResult<PagedResult<LookupItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (await Denied(ViewPermission, cancellationToken) is { } denied) return denied;

        var result = await _service.SearchAsync(search, isActive, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<LookupOptionDto>>> Options(CancellationToken cancellationToken)
    {
        if (await Denied(ViewPermission, cancellationToken) is { } denied) return denied;

        return Ok(await _service.ListOptionsAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LookupItemDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (await Denied(ViewPermission, cancellationToken) is { } denied) return denied;

        var item = await _service.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<LookupItemDto>> Create(CreateLookupRequest request, CancellationToken cancellationToken)
    {
        if (await Denied(ManagePermission, cancellationToken) is { } denied) return denied;

        var result = await _service.CreateAsync(request, cancellationToken);
        return FromResult(result, created: true);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LookupItemDto>> Update(Guid id, UpdateLookupRequest request, CancellationToken cancellationToken)
    {
        if (await Denied(ManagePermission, cancellationToken) is { } denied) return denied;

        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return FromResult(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await Denied(ManagePermission, cancellationToken) is { } denied) return denied;

        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<LookupItemDto> FromResult(LookupOperationResult result, bool created) => result.Status switch
    {
        LookupOperationStatus.Success when created =>
            CreatedAtAction(nameof(GetById), new { id = result.Item!.Id }, result.Item),
        LookupOperationStatus.Success => Ok(result.Item),
        LookupOperationStatus.NotFound => NotFound(),
        LookupOperationStatus.DuplicateCode => Conflict(new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };

    private async Task<ObjectResult?> Denied(string permission, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } userId)
        {
            return new ObjectResult(new { message = "Niet geauthenticeerd." }) { StatusCode = StatusCodes.Status401Unauthorized };
        }

        if (!await _authorization.UserHasPermissionAsync(userId, permission, cancellationToken))
        {
            return new ObjectResult(new { message = $"Missing permission: {permission}" }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        return null;
    }
}
