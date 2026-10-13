using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>
/// PIN-beheer per medewerker. Een beheerder kan een code zetten, genereren of
/// intrekken maar NOOIT uitlezen: de respons bevat een code uitsluitend éénmalig bij
/// genereren, en de status-endpoint geeft alleen metadata (bestaat/actief/lockout).
/// </summary>
[ApiController]
[Authorize]
[Route("api/employees/{employeeId:guid}/attendance-credential")]
public class AttendanceCredentialsController : ControllerBase
{
    private readonly IAttendanceCredentialService _credentialService;

    public AttendanceCredentialsController(IAttendanceCredentialService credentialService) =>
        _credentialService = credentialService;

    [HttpGet]
    [RequirePermission(PermissionCodes.AttendanceManageCredentials)]
    public async Task<ActionResult<AttendanceCredentialStatusDto>> GetStatus(
        Guid employeeId, CancellationToken cancellationToken) =>
        Ok(await _credentialService.GetStatusAsync(employeeId, cancellationToken));

    [HttpPut]
    [RequirePermission(PermissionCodes.AttendanceManageCredentials)]
    public async Task<ActionResult<AttendanceCredentialResult>> SetPin(
        Guid employeeId, [FromBody] SetAttendancePinRequest request, CancellationToken cancellationToken) =>
        Handle(await _credentialService.SetPinAsync(employeeId, request.Pin, cancellationToken));

    [HttpDelete]
    [RequirePermission(PermissionCodes.AttendanceManageCredentials)]
    public async Task<ActionResult<AttendanceCredentialResult>> Disable(
        Guid employeeId, CancellationToken cancellationToken) =>
        Handle(await _credentialService.DisableAsync(employeeId, cancellationToken));

    private ActionResult<AttendanceCredentialResult> Handle(AttendanceCredentialResult result) => result.Outcome switch
    {
        AttendanceCredentialOutcome.Success => Ok(result),
        AttendanceCredentialOutcome.EmployeeNotFound or AttendanceCredentialOutcome.NoCredential =>
            NotFound(new { message = result.Error }),
        AttendanceCredentialOutcome.NotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };
}
