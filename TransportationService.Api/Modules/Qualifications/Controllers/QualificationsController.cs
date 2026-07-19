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
    public async Task<ActionResult<IReadOnlyList<ExpiringQualificationDto>>> ListExpiring([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        return Ok(await _qualificationService.ListExpiringWithinDaysAsync(Math.Clamp(days, 1, 365), cancellationToken));
    }

    [HttpGet("api/qualifications/expired")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<ExpiringQualificationDto>>> ListExpired(CancellationToken cancellationToken)
    {
        return Ok(await _qualificationService.ListExpiredAsync(cancellationToken));
    }

    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedDocumentExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    [HttpPost("api/employees/{employeeId:guid}/qualifications/{id:guid}/document")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsEdit)]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<ActionResult<EmployeeQualificationDto>> UploadDocument(
        Guid employeeId, Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxDocumentBytes)
        {
            return BadRequest(new { message = "Het document moet tussen 1 byte en 10 MB groot zijn." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedDocumentExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan." });
        }

        await using var stream = file.OpenReadStream();
        var updated = await _qualificationService.AttachDocumentAsync(employeeId, id, file.FileName, stream, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("api/employees/{employeeId:guid}/qualifications/{id:guid}/document")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<IActionResult> DownloadDocument(Guid employeeId, Guid id, CancellationToken cancellationToken)
    {
        var document = await _qualificationService.OpenDocumentAsync(employeeId, id, cancellationToken);
        if (document is not { } found)
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(found.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        return File(found.Content, contentType, found.FileName);
    }

    [HttpDelete("api/employees/{employeeId:guid}/qualifications/{id:guid}/document")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsDelete)]
    public async Task<IActionResult> DeleteDocument(Guid employeeId, Guid id, CancellationToken cancellationToken)
    {
        var removed = await _qualificationService.RemoveDocumentAsync(employeeId, id, cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    [HttpGet("api/qualification-types")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<QualificationTypeDto>>> ListQualificationTypes(CancellationToken cancellationToken)
    {
        return Ok(await _qualificationService.ListQualificationTypesAsync(cancellationToken));
    }
}
