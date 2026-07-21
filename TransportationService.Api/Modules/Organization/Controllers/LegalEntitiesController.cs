using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Organization.Dtos;
using TransportationService.Api.Modules.Organization.Services;

namespace TransportationService.Api.Modules.Organization.Controllers;

[ApiController]
public class LegalEntitiesController : ControllerBase
{
    private const long MaxLogoBytes = 2 * 1024 * 1024;
    private static readonly string[] AllowedLogoExtensions = [".png", ".jpg", ".jpeg", ".svg"];

    private readonly ILegalEntityService _service;

    public LegalEntitiesController(ILegalEntityService service)
    {
        _service = service;
    }

    [HttpGet("api/legal-entities")]
    [RequirePermission(PermissionCodes.LegalEntitiesView)]
    public async Task<ActionResult<IReadOnlyList<LegalEntityDto>>> List([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListAsync(includeInactive, cancellationToken));
    }

    /// <summary>Active entities for pickers. Any authenticated user: order/invoice forms need the list.</summary>
    [HttpGet("api/legal-entities/options")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<LegalEntityOptionDto>>> Options(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListOptionsAsync(cancellationToken));
    }

    [HttpGet("api/legal-entities/{id:guid}")]
    [RequirePermission(PermissionCodes.LegalEntitiesView)]
    public async Task<ActionResult<LegalEntityDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _service.GetAsync(id, cancellationToken);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpPost("api/legal-entities")]
    [RequirePermission(PermissionCodes.LegalEntitiesManage)]
    public async Task<ActionResult<LegalEntityDto>> Create(SaveLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("api/legal-entities/{id:guid}")]
    [RequirePermission(PermissionCodes.LegalEntitiesManage)]
    public async Task<ActionResult<LegalEntityDto>> Update(Guid id, SaveLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("api/legal-entities/{id:guid}/active")]
    [RequirePermission(PermissionCodes.LegalEntitiesManage)]
    public async Task<ActionResult<LegalEntityDto>> SetActive(Guid id, SetLegalEntityActiveRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.SetActiveAsync(id, request.IsActive, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("api/legal-entities/{id:guid}/logo")]
    [RequirePermission(PermissionCodes.LegalEntitiesManage)]
    [RequestSizeLimit(MaxLogoBytes + 1024)]
    public async Task<ActionResult<LegalEntityDto>> UploadLogo(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxLogoBytes)
        {
            return BadRequest(new { message = "Het logo moet tussen 1 byte en 2 MB groot zijn." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedLogoExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Alleen PNG-, JPG- en SVG-bestanden zijn toegestaan." });
        }

        var contentType = extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };

        await using var stream = file.OpenReadStream();
        var updated = await _service.AttachLogoAsync(id, file.FileName, contentType, stream, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("api/legal-entities/{id:guid}/logo")]
    [Authorize]
    public async Task<IActionResult> DownloadLogo(Guid id, CancellationToken cancellationToken)
    {
        var logo = await _service.OpenLogoAsync(id, cancellationToken);
        if (logo is not { } found)
        {
            return NotFound();
        }

        return File(found.Content, found.ContentType, found.FileName);
    }

    [HttpDelete("api/legal-entities/{id:guid}/logo")]
    [RequirePermission(PermissionCodes.LegalEntitiesManage)]
    public async Task<ActionResult<LegalEntityDto>> RemoveLogo(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _service.RemoveLogoAsync(id, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>User-scoped active-entity preference; auth-only (cosmetic default, never authorization).</summary>
    [HttpGet("api/me/active-legal-entity")]
    [Authorize]
    public async Task<ActionResult<ActiveLegalEntityDto>> GetActiveSelection(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetActiveSelectionAsync(cancellationToken));
    }

    [HttpPut("api/me/active-legal-entity")]
    [Authorize]
    public async Task<ActionResult<ActiveLegalEntityDto>> SetActiveSelection(ActiveLegalEntityDto request, CancellationToken cancellationToken)
    {
        return Ok(await _service.SetActiveSelectionAsync(request.LegalEntityId, cancellationToken));
    }
}

public record SetLegalEntityActiveRequest(bool IsActive);
