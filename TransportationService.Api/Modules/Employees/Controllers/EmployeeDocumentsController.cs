using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Employees.Controllers;

[ApiController]
[Route("api/employees/{employeeId:guid}/documents")]
public class EmployeeDocumentsController : ControllerBase
{
    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly IEmployeeDocumentService _service;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    public EmployeeDocumentsController(IEmployeeDocumentService service,
        ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
    {
        _service = service;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    /// <summary>Sensitive categories (ID/medical/contract) need employee_documents.view_sensitive.</summary>
    private async Task<bool> HasSensitiveAccessAsync(CancellationToken cancellationToken)
        => _currentUser.CurrentUserId is { } userId
           && await _authorization.UserHasPermissionAsync(userId, PermissionCodes.EmployeeDocumentsViewSensitive, cancellationToken);

    [HttpGet]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<EmployeeDocumentDto>>> List(
        Guid employeeId, [FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var documents = await _service.ListAsync(
            employeeId, await HasSensitiveAccessAsync(cancellationToken), includeArchived, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EmployeeDocumentsCreate)]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<ActionResult<EmployeeDocumentDto>> Upload(
        Guid employeeId,
        IFormFile file,
        [FromForm] EmployeeDocumentCategory category,
        [FromForm] string? customLabel,
        [FromForm] DateOnly? expiryDate,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
    {
        if (Validate(file) is { } error)
        {
            return BadRequest(new { message = error });
        }

        // Uploading a sensitive-category document requires the sensitive permission too.
        if (_service.IsSensitive(category) && !await HasSensitiveAccessAsync(cancellationToken))
        {
            return Forbid();
        }

        await using var stream = file.OpenReadStream();
        var uploaded = await _service.UploadAsync(employeeId,
            new SaveEmployeeDocumentMetadata(category, customLabel, expiryDate, notes),
            file.FileName, ContentTypeFor(file.FileName), file.Length, stream, cancellationToken);
        return uploaded is null ? NotFound() : Ok(uploaded);
    }

    [HttpPut("{documentId:guid}")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsEdit)]
    public async Task<ActionResult<EmployeeDocumentDto>> UpdateMetadata(
        Guid employeeId, Guid documentId, UpdateEmployeeDocumentRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateMetadataAsync(employeeId, documentId,
            new SaveEmployeeDocumentMetadata(request.Category, request.CustomLabel, request.ExpiryDate, request.Notes),
            cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{documentId:guid}/file")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsEdit)]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<ActionResult<EmployeeDocumentDto>> Replace(
        Guid employeeId, Guid documentId, IFormFile file, CancellationToken cancellationToken)
    {
        if (Validate(file) is { } error)
        {
            return BadRequest(new { message = error });
        }

        await using var stream = file.OpenReadStream();
        var updated = await _service.ReplaceFileAsync(employeeId, documentId,
            file.FileName, ContentTypeFor(file.FileName), file.Length, stream, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{documentId:guid}/archived")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsEdit)]
    public async Task<ActionResult<EmployeeDocumentDto>> SetArchived(
        Guid employeeId, Guid documentId, SetArchivedRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.SetArchivedAsync(employeeId, documentId, request.Archived, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("{documentId:guid}/content")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsView)]
    public async Task<IActionResult> Download(Guid employeeId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _service.OpenAsync(employeeId, documentId, cancellationToken);
        if (document is not { } found)
        {
            return NotFound();
        }

        if (found.IsSensitive && !await HasSensitiveAccessAsync(cancellationToken))
        {
            await found.Content.DisposeAsync();
            return Forbid();
        }

        return File(found.Content, found.ContentType, found.FileName);
    }

    [HttpDelete("{documentId:guid}")]
    [RequirePermission(PermissionCodes.EmployeeDocumentsDelete)]
    public async Task<IActionResult> Delete(Guid employeeId, Guid documentId, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(employeeId, documentId, cancellationToken) ? NoContent() : NotFound();
    }

    private static string? Validate(IFormFile file)
    {
        if (file.Length == 0 || file.Length > MaxDocumentBytes)
        {
            return "Het document moet tussen 1 byte en 10 MB groot zijn.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension)
            ? null
            : "Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan.";
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}

public record UpdateEmployeeDocumentRequest(
    EmployeeDocumentCategory Category, string? CustomLabel, DateOnly? ExpiryDate, string? Notes);

public record SetArchivedRequest(bool Archived);
