using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Services;

namespace TransportationService.Api.Modules.Tarification.Controllers;

/// <summary>
/// Rate-table (tarieventabel) Excel export and validated round-trip import: export doubles as
/// the import template (RegelId column is the round-trip key), preview never writes, commit is
/// transactional. Mirrors the customer import endpoints under Modules/Partners.
/// </summary>
[ApiController]
public class PricingImportController : ControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private readonly IPricingExcelService _excelService;

    public PricingImportController(IPricingExcelService excelService)
    {
        _excelService = excelService;
    }

    [HttpGet("api/pricing/agreements/{id:guid}/export")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage, PermissionCodes.TariffsImport)]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var (file, fileName) = await _excelService.ExportAsync(id, cancellationToken);
        return file is null
            ? NotFound()
            : File(file, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpPost("api/pricing/agreements/{id:guid}/import/preview")]
    [RequirePermission(PermissionCodes.TariffsImport, PermissionCodes.TariffsManage)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<IActionResult> Preview(
        Guid id, IFormFile file, [FromForm] Guid? profileId, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return BadRequest(new { message = "Het bestand moet tussen 1 byte en 5 MB groot zijn." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        await using var stream = file.OpenReadStream();
        var (preview, error) = await _excelService.PreviewAsync(id, stream, profileId, file.FileName, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : Ok(preview);
    }

    /// <summary>Sprint 4: the header texts of an uploaded file, for the mapping step.</summary>
    [HttpPost("api/pricing/import/headers")]
    [RequirePermission(PermissionCodes.TariffsImport, PermissionCodes.TariffsManage)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<IActionResult> Headers(IFormFile file, [FromForm] Guid? profileId, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return BadRequest(new { message = "Het bestand moet tussen 1 byte en 5 MB groot zijn." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        await using var stream = file.OpenReadStream();
        var (headers, error) = await _excelService.ReadHeadersAsync(stream, profileId, cancellationToken);
        return error is not null
            ? BadRequest(new { message = error })
            : Ok(new { headers, fields = PricingImportColumns.All });
    }

    [HttpPost("api/pricing/agreements/{id:guid}/import/commit")]
    [RequirePermission(PermissionCodes.TariffsImport, PermissionCodes.TariffsManage)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<IActionResult> Commit(
        Guid id, IFormFile file,
        [FromForm] string mode,
        [FromForm] bool applyRemovals,
        [FromForm] string? newName,
        [FromForm] DateOnly? newEffectiveFrom,
        [FromForm] Guid? profileId,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return BadRequest(new { message = "Het bestand moet tussen 1 byte en 5 MB groot zijn." });
        }

        if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<PricingImportMode>(mode, out var parsedMode))
        {
            return BadRequest(new { message = "Onbekende importmodus." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        await using var stream = file.OpenReadStream();
        var request = new PricingImportCommitRequest(parsedMode, applyRemovals, newName, newEffectiveFrom);
        var (result, error) = await _excelService.CommitAsync(id, request, stream, profileId, file.FileName, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : result is null ? NotFound() : Ok(result);
    }
}
