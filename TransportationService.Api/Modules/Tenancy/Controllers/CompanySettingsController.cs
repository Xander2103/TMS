using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tenancy.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy.Controllers;

[ApiController]
[Route("api/company-settings")]
public class CompanySettingsController : ControllerBase
{
    private readonly ICompanySettingsService _service;

    public CompanySettingsController(ICompanySettingsService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CompanySettingsView)]
    public async Task<ActionResult<CompanySettingsDto>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetAsync(cancellationToken));
    }

    /// <summary>Regional display preferences for EVERY signed-in user (the central date
    /// formatter needs them app-wide) — deliberately without a permission gate: nothing in
    /// here is sensitive, it is presentation configuration only.</summary>
    [HttpGet("display")]
    [Authorize]
    public async Task<ActionResult<DisplayPreferencesDto>> Display(CancellationToken cancellationToken)
    {
        var settings = await _service.GetAsync(cancellationToken);
        return Ok(new DisplayPreferencesDto(
            settings.DateFormat, settings.DecimalSeparator, settings.Timezone, settings.DefaultLanguage));
    }

    [HttpPut]
    [RequirePermission(PermissionCodes.CompanySettingsManage)]
    public async Task<ActionResult<CompanySettingsDto>> Update(UpdateCompanySettingsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DefaultCurrency) || request.DefaultCurrency.Trim().Length != 3)
        {
            return BadRequest(new { message = "Een geldige valutacode van 3 letters is verplicht (bv. EUR)." });
        }

        return Ok(await _service.UpdateAsync(request, cancellationToken));
    }
}
