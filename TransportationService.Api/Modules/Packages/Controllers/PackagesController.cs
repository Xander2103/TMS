using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Labels;
using TransportationService.Api.Modules.Packages.Services;

namespace TransportationService.Api.Modules.Packages.Controllers;

/// <summary>
/// Package (colli) management. Barcode knowledge alone never grants access: every route is
/// permission-gated and tenant-scoped; scans go through the scanning endpoints instead.
/// </summary>
[ApiController]
public class PackagesController : ControllerBase
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    private readonly IPackageService _service;
    private readonly IPackageImportService _importService;
    private readonly IPackageLabelService _labelService;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public PackagesController(
        IPackageService service,
        IPackageImportService importService,
        IPackageLabelService labelService,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _importService = importService;
        _labelService = labelService;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    [HttpGet("api/transport-orders/{orderId:guid}/packages")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<IReadOnlyList<PackageDto>>> ListForOrder(Guid orderId, CancellationToken cancellationToken) =>
        Ok(await _service.ListForOrderAsync(orderId, cancellationToken));

    [HttpPost("api/transport-orders/{orderId:guid}/packages")]
    [RequirePermission(PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<PackageDto>> Create(
        Guid orderId, CreatePackageRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.CreateAsync(orderId, request, cancellationToken));

    [HttpGet("api/packages/{id:guid}")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<PackageDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var package = await _service.GetByIdAsync(id, cancellationToken);
        return package is null ? NotFound() : Ok(package);
    }

    [HttpGet("api/packages/{id:guid}/barcodes")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<IReadOnlyList<PackageBarcodeDto>>> Barcodes(Guid id, CancellationToken cancellationToken)
    {
        var package = await _service.GetByIdAsync(id, cancellationToken);
        return package is null ? NotFound() : Ok(await _service.ListBarcodesAsync(id, cancellationToken));
    }

    [HttpPut("api/packages/{id:guid}")]
    [RequirePermission(PermissionCodes.PackagesManage)]
    public async Task<ActionResult<PackageDto>> Update(
        Guid id, UpdatePackageRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.UpdateAsync(id, request, cancellationToken));

    [HttpPost("api/packages/{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.PackagesCancel, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<PackageDto>> Cancel(
        Guid id, CancelPackageRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.CancelAsync(id, request, cancellationToken));

    [HttpPost("api/packages/{id:guid}/relabel")]
    [RequirePermission(PermissionCodes.PackagesRelabel)]
    public async Task<ActionResult<PackageDto>> Relabel(
        Guid id, RelabelPackageRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.RelabelAsync(id, request, cancellationToken));

    [HttpPost("api/transport-orders/{orderId:guid}/packages/bulk")]
    [RequirePermission(PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<BulkCreateResultDto>> BulkCreate(
        Guid orderId, BulkCreatePackagesRequest request, CancellationToken cancellationToken)
    {
        var (result, error) = await _service.BulkCreateAsync(orderId, request, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : Ok(result);
    }

    [HttpGet("api/package-import/template")]
    [RequirePermission(PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage)]
    public IActionResult Template() => File(
        _importService.BuildTemplate(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "colli-import-sjabloon.xlsx");

    [HttpPost("api/transport-orders/{orderId:guid}/packages/import/preview")]
    [RequirePermission(PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage)]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<ActionResult<ImportPreviewDto>> ImportPreview(
        Guid orderId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is 0 or > MaxImportBytes)
        {
            return BadRequest(new { message = "Bestand ontbreekt of is groter dan 5 MB." });
        }

        await using var stream = file.OpenReadStream();
        var (preview, error) = await _importService.PreviewAsync(orderId, stream, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : Ok(preview);
    }

    [HttpPost("api/transport-orders/{orderId:guid}/packages/import")]
    [RequirePermission(PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage)]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<ActionResult<ImportCommitDto>> ImportCommit(
        Guid orderId, IFormFile file,
        [FromQuery] bool allOrNothing = true, [FromQuery] bool allowUpdates = false,
        CancellationToken cancellationToken = default)
    {
        if (file.Length is 0 or > MaxImportBytes)
        {
            return BadRequest(new { message = "Bestand ontbreekt of is groter dan 5 MB." });
        }

        await using var stream = file.OpenReadStream();
        var (result, error) = await _importService.CommitAsync(orderId, stream, allOrNothing, allowUpdates, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : Ok(result);
    }

    public record PrintLabelsRequest(
        IReadOnlyList<Guid> PackageIds, LabelFormat Format = LabelFormat.Thermal100x150, string? ReprintReason = null);

    [HttpPost("api/packages/labels")]
    [RequirePermission(PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage, PermissionCodes.PackagesRelabel)]
    public async Task<IActionResult> PrintLabels(PrintLabelsRequest request, CancellationToken cancellationToken)
    {
        // Reprints (any package that already has a label version) require packages.relabel.
        var hasRelabel = _currentUserContext.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.PackagesRelabel, cancellationToken);
        if (!hasRelabel && request.PackageIds.Count > 0)
        {
            var anyExisting = await _labelService.ListVersionsAsync(request.PackageIds[0], cancellationToken);
            var reprint = anyExisting.Count > 0;
            if (!reprint)
            {
                foreach (var id in request.PackageIds.Skip(1))
                {
                    if ((await _labelService.ListVersionsAsync(id, cancellationToken)).Count > 0)
                    {
                        reprint = true;
                        break;
                    }
                }
            }

            if (reprint)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Herafdrukken vereist het recht packages.relabel." });
            }
        }

        var (pdf, error) = await _labelService.PrintAsync(
            request.PackageIds, request.Format, request.ReprintReason, cancellationToken);
        return error is not null
            ? BadRequest(new { message = error })
            : File(pdf!, "application/pdf", "etiketten.pdf");
    }

    [HttpGet("api/packages/{id:guid}/labels")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.PackagesManage)]
    public async Task<ActionResult<IReadOnlyList<PackageLabelVersionDto>>> LabelVersions(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await _labelService.ListVersionsAsync(id, cancellationToken));

    [HttpGet("api/packages/{id:guid}/labels/{version:int}/pdf")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.PackagesManage)]
    public async Task<IActionResult> LabelVersionPdf(Guid id, int version, CancellationToken cancellationToken)
    {
        var pdf = await _labelService.RenderVersionAsync(id, version, cancellationToken);
        return pdf is null ? NotFound() : File(pdf, "application/pdf", $"etiket-v{version}.pdf");
    }

    private ActionResult<PackageDto> Handle(PackageOperationResult result) => result.Outcome switch
    {
        PackageOutcome.Success => Ok(result.Package),
        PackageOutcome.NotFound => NotFound(),
        PackageOutcome.InvalidState => Conflict(new { message = result.Error }),
        PackageOutcome.DuplicateBarcode => Conflict(new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };
}
