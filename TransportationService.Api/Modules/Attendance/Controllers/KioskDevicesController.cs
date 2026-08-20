using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>
/// Beheer van prikklok-devices. De deviceKey verschijnt uitsluitend éénmalig in de
/// respons van provisioning of rotatie; daarna bestaat server-side alleen de hash.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/kiosks")]
public class KioskDevicesController : ControllerBase
{
    private readonly IKioskDeviceService _deviceService;

    public KioskDevicesController(IKioskDeviceService deviceService) => _deviceService = deviceService;

    [HttpGet]
    [RequirePermission(PermissionCodes.AttendanceManageKiosks)]
    public async Task<ActionResult<IReadOnlyList<KioskDeviceDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _deviceService.ListAsync(cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.AttendanceManageKiosks)]
    public async Task<ActionResult<KioskProvisionResult>> Create(
        [FromBody] SaveKioskDeviceRequest request, CancellationToken cancellationToken)
    {
        var result = await _deviceService.CreateAsync(request, cancellationToken);
        return result is null
            ? BadRequest(new { message = "Een naam is verplicht." })
            : Ok(result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.AttendanceManageKiosks)]
    public async Task<ActionResult<KioskDeviceDto>> Update(
        Guid id, [FromBody] SaveKioskDeviceRequest request, CancellationToken cancellationToken)
    {
        var result = await _deviceService.UpdateAsync(id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/rotate-secret")]
    [RequirePermission(PermissionCodes.AttendanceManageKiosks)]
    public async Task<ActionResult<KioskProvisionResult>> RotateSecret(Guid id, CancellationToken cancellationToken)
    {
        var result = await _deviceService.RotateSecretAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
