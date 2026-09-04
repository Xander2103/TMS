using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.OrderImport.Services;

namespace TransportationService.Api.Modules.OrderImport.Controllers;

/// <summary>
/// Automated Excel ORDER import (P13): mapping profiles, batch history and the upload
/// endpoint (with a "Enkel valideren" dry run). Creation goes through the regular order
/// service, so imported orders get numbering, wrapper dossiers, pricing and audit exactly
/// like manually entered ones.
/// </summary>
[ApiController]
[Route("api/order-imports")]
public class OrderImportsController : ControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".xlsx"];

    private readonly IOrderImportService _importService;

    public OrderImportsController(IOrderImportService importService)
    {
        _importService = importService;
    }

    [HttpGet("profiles")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<OrderImportProfileDto>>> Profiles(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        return Ok(await _importService.ListProfilesAsync(cancellationToken, includeInactive));
    }

    /// <summary>The importable TMS target fields (stable keys + picker groups; labels are client-side i18n).</summary>
    [HttpGet("fields")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    public ActionResult<IReadOnlyList<OrderImportFieldDto>> Fields() =>
        Ok(OrderImportFields.All.Select(f => new OrderImportFieldDto(f.Key, f.Group)).ToList());

    [HttpPost("profiles")]
    [RequirePermission(PermissionCodes.OrderImportsManageProfiles, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<OrderImportProfileDto>> CreateProfile(
        SaveOrderImportProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await _importService.CreateProfileAsync(request, cancellationToken));

    [HttpPut("profiles/{id:guid}")]
    [RequirePermission(PermissionCodes.OrderImportsManageProfiles, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<OrderImportProfileDto>> UpdateProfile(
        Guid id, SaveOrderImportProfileRequest request, CancellationToken cancellationToken)
    {
        var updated = await _importService.UpdateProfileAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("profiles/{id:guid}")]
    [RequirePermission(PermissionCodes.OrderImportsManageProfiles, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> DeleteProfile(Guid id, CancellationToken cancellationToken) =>
        await _importService.DeleteProfileAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>
    /// Reads a sample workbook's headers + a few example values per column, suggests target
    /// fields (deterministic alias catalog) and scores saved profiles against the headers.
    /// Never persists anything.
    /// </summary>
    [HttpPost("analyze")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage, PermissionCodes.OrderImportsManageProfiles)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<ActionResult<OrderImportAnalysisDto>> Analyze(IFormFile file, CancellationToken cancellationToken)
    {
        if (Modules.Security.UploadValidation.Validate(file, MaxUploadBytes, AllowedExtensions,
                extensionError: "Alleen Excel-bestanden (.xlsx) zijn toegestaan.") is { } uploadError)
        {
            return BadRequest(new { message = uploadError });
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return Ok(await _importService.AnalyzeAsync(buffer.ToArray(), cancellationToken));
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<PagedResult<OrderImportBatchDto>>> Batches(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        return Ok(await _importService.ListBatchesAsync(page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<OrderImportBatchDetailDto>> Batch(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _importService.GetBatchAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<ActionResult<OrderImportBatchDetailDto>> Import(
        IFormFile file,
        [FromForm] Guid profileId,
        [FromForm] Guid customerId,
        [FromForm] bool dryRun,
        CancellationToken cancellationToken)
    {
        if (Modules.Security.UploadValidation.Validate(file, MaxUploadBytes, AllowedExtensions,
                extensionError: "Alleen Excel-bestanden (.xlsx) zijn toegestaan.") is { } uploadError)
        {
            return BadRequest(new { message = uploadError });
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var detail = await _importService.ImportAsync(
            profileId, customerId, file.FileName, buffer.ToArray(), dryRun, cancellationToken);
        return Ok(detail);
    }
}
