using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>Urenregistratie-instellingen (per tenant, HrReminderSettings-patroon).</summary>
[ApiController]
[Authorize]
[Route("api/attendance/settings")]
public class AttendanceSettingsController : ControllerBase
{
    private readonly IAttendanceSettingsService _settingsService;

    public AttendanceSettingsController(IAttendanceSettingsService settingsService) =>
        _settingsService = settingsService;

    [HttpGet]
    [RequirePermission(PermissionCodes.AttendanceManageSettings)]
    public async Task<ActionResult<AttendanceSettingsDto>> Get(CancellationToken cancellationToken) =>
        Ok(await _settingsService.GetAsync(cancellationToken));

    [HttpPut]
    [RequirePermission(PermissionCodes.AttendanceManageSettings)]
    public async Task<ActionResult<AttendanceSettingsDto>> Update(
        [FromBody] UpdateAttendanceSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await _settingsService.UpdateAsync(request, cancellationToken));
}
