using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Services;

namespace TransportationService.Api.Modules.Peppol.Controllers;

/// <summary>
/// Per-legal-entity Peppol configuration and the tenant-wide overview. Everything provider-
/// specific stays behind <see cref="IPeppolProvider"/>; no secrets pass through these
/// endpoints (the sandbox provider has none, real credentials live in configuration only).
/// </summary>
[ApiController]
[Route("api/peppol")]
public class PeppolController : ControllerBase
{
    private readonly IPeppolSettingsService _settingsService;

    public PeppolController(IPeppolSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Configuration checklist per legal entity, transmission counts and customer-data gaps.</summary>
    [HttpGet("overview")]
    [RequirePermission(PermissionCodes.PeppolView)]
    public async Task<ActionResult<PeppolOverviewDto>> Overview(CancellationToken cancellationToken)
        => Ok(await _settingsService.GetOverviewAsync(cancellationToken));

    [HttpGet("settings/{legalEntityId:guid}")]
    [RequirePermission(PermissionCodes.PeppolView)]
    public async Task<ActionResult<PeppolSettingsDto>> GetSettings(Guid legalEntityId, CancellationToken cancellationToken)
        => Ok(await _settingsService.GetSettingsAsync(legalEntityId, cancellationToken));

    [HttpPut("settings/{legalEntityId:guid}")]
    [RequirePermission(PermissionCodes.PeppolConfigure)]
    public async Task<ActionResult<PeppolSettingsDto>> SaveSettings(
        Guid legalEntityId, SavePeppolSettingsRequest request, CancellationToken cancellationToken)
        => Ok(await _settingsService.SaveSettingsAsync(legalEntityId, request, cancellationToken));

    /// <summary>Validates the legal entity's OWN participant identity at the configured provider.</summary>
    [HttpPost("settings/{legalEntityId:guid}/test-connection")]
    [RequirePermission(PermissionCodes.PeppolValidate)]
    public async Task<ActionResult<PeppolTestConnectionResultDto>> TestConnection(
        Guid legalEntityId, CancellationToken cancellationToken)
        => Ok(await _settingsService.TestConnectionAsync(legalEntityId, cancellationToken));
}
