using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Services;

namespace TransportationService.Api.Modules.Qualifications.Controllers;

[ApiController]
public class QualificationsController : ControllerBase
{
    private readonly IQualificationService _qualificationService;
    private readonly ICurrentUserContext _currentUserContext;

    public QualificationsController(IQualificationService qualificationService, ICurrentUserContext currentUserContext)
    {
        _qualificationService = qualificationService;
        _currentUserContext = currentUserContext;
    }

    [HttpGet("api/employees/{employeeId:guid}/qualifications")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<EmployeeQualificationDto>>> ListForEmployee(Guid employeeId, CancellationToken cancellationToken)
    {
        return Ok(await _qualificationService.ListForEmployeeAsync(employeeId, cancellationToken));
    }

    [HttpPost("api/employees/{employeeId:guid}/qualifications")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsCreate)]
    public async Task<ActionResult<EmployeeQualificationDto>> Create(Guid employeeId, CreateEmployeeQualificationRequest request, CancellationToken cancellationToken)
    {
        var created = await _qualificationService.CreateAsync(employeeId, request, cancellationToken);
        return CreatedAtAction(nameof(ListForEmployee), new { employeeId }, created);
    }

    [HttpPut("api/employees/{employeeId:guid}/qualifications/{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsEdit)]
    public async Task<ActionResult<EmployeeQualificationDto>> Update(Guid employeeId, Guid id, UpdateEmployeeQualificationRequest request, CancellationToken cancellationToken)
    {
        var updated = await _qualificationService.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("api/employees/{employeeId:guid}/qualifications/{id:guid}/verify")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsApprove)]
    public async Task<ActionResult<EmployeeQualificationDto>> Verify(Guid employeeId, Guid id, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return Unauthorized();
        }

        var verified = await _qualificationService.VerifyAsync(id, userId, cancellationToken);
        return verified is null ? NotFound() : Ok(verified);
    }

    [HttpPost("api/employees/{employeeId:guid}/qualifications/{id:guid}/suspend")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsEdit)]
    public async Task<ActionResult<EmployeeQualificationDto>> Suspend(Guid employeeId, Guid id, CancellationToken cancellationToken)
    {
        var suspended = await _qualificationService.SuspendAsync(id, cancellationToken);
        return suspended is null ? NotFound() : Ok(suspended);
    }

    [HttpGet("api/qualifications/expiring")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<EmployeeQualificationDto>>> ListExpiring([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        return Ok(await _qualificationService.ListExpiringWithinDaysAsync(days, cancellationToken));
    }

    [HttpGet("api/qualifications/expired")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<EmployeeQualificationDto>>> ListExpired(CancellationToken cancellationToken)
    {
        return Ok(await _qualificationService.ListExpiredAsync(cancellationToken));
    }

    [HttpGet("api/qualification-types")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<QualificationTypeDto>>> ListQualificationTypes(CancellationToken cancellationToken)
    {
        return Ok(await _qualificationService.ListQualificationTypesAsync(cancellationToken));
    }
}
