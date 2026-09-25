using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>
/// Order documents: metadata nested under the order, file ops on the flat route.
/// <para>
/// D6: the flat <c>api/order-documents/{id}</c> routes also serve DOSSIER-level documents (one
/// entity, one file). Their <c>[RequirePermission]</c> is only the coarse outer gate (any of the
/// order OR dossier rights); the real decision depends on the row's scope and is taken in ONE
/// place, <see cref="IOrderDocumentAccessResolver"/>, after which the service runs in exactly the
/// authorized scope — a dossier document is never reachable through an order-scoped call.
/// </para>
/// </summary>
[ApiController]
public class TransportOrderDocumentsController : ControllerBase
{
    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly ITransportOrderDocumentService _service;
    private readonly IOrderDocumentAccessResolver _access;

    public TransportOrderDocumentsController(ITransportOrderDocumentService service, IOrderDocumentAccessResolver access)
    {
        _service = service;
        _access = access;
    }

    [HttpGet("api/transport-orders/{orderId:guid}/documents")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<TransportOrderDocumentDto>>> List(Guid orderId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListAsync(orderId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpPost("api/transport-orders/{orderId:guid}/documents")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDocumentDto>> Create(
        Guid orderId, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(orderId, request, cancellationToken);
        return created is null ? NotFound() : Ok(created);
    }

    // --- D6: dossier level (contract 4.2) ---

    /// <summary>Every document of the dossier, both scopes, each exactly once.</summary>
    [HttpGet("api/dossiers/{dossierId:guid}/documents")]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<IReadOnlyList<DossierDocumentDto>>> ListForDossier(Guid dossierId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListForDossierAsync(dossierId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    /// <summary><c>transportOrderId: null</c> = a document of the dossier as a whole; else an order OF THIS dossier.</summary>
    [HttpPost("api/dossiers/{dossierId:guid}/documents")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDocumentDto>> CreateForDossier(
        Guid dossierId, CreateDossierDocumentRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateForDossierAsync(dossierId, request, cancellationToken);
        return created is null ? NotFound() : Ok(created);
    }

    /// <summary>
    /// Changes the level a document hangs on inside ITS dossier (null = dossier level). Needs the
    /// write right of BOTH the source and the target scope; the file is not touched.
    /// </summary>
    [HttpPost("api/order-documents/{id:guid}/move")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDocumentDto>> Move(
        Guid id, MoveOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        var source = await _access.AuthorizeAsync(id, OrderDocumentOperation.Write, cancellationToken);
        if (Refusal(source) is { } sourceRefusal)
        {
            return sourceRefusal;
        }

        var targetScope = request.TargetTransportOrderId is null ? DossierDocumentScope.Dossier : DossierDocumentScope.Order;
        var target = await _access.AuthorizeScopeAsync(targetScope, OrderDocumentOperation.Write, cancellationToken);
        if (Refusal(target) is { } targetRefusal)
        {
            return targetRefusal;
        }

        var moved = await _service.MoveAsync(id, request.TargetTransportOrderId, cancellationToken);
        return moved is null ? NotFound() : Ok(moved);
    }

    // --- flat routes: scope decides the required right (OrderDocumentAccessResolver) ---

    [HttpPut("api/order-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<TransportOrderDocumentDto>> Update(
        Guid id, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        var access = await _access.AuthorizeAsync(id, OrderDocumentOperation.Write, cancellationToken);
        if (Refusal(access) is { } refusal)
        {
            return refusal;
        }

        var updated = await _service.UpdateAsync(id, request, cancellationToken, access.Scope);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/order-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.DossiersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var access = await _access.AuthorizeAsync(id, OrderDocumentOperation.Write, cancellationToken);
        if (Refusal(access) is { } refusal)
        {
            return refusal;
        }

        return await _service.DeleteAsync(id, cancellationToken, access.Scope) ? NoContent() : NotFound();
    }

    [HttpPost("api/order-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.DossiersManage)]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<ActionResult<TransportOrderDocumentDto>> UploadFile(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var access = await _access.AuthorizeAsync(id, OrderDocumentOperation.Upload, cancellationToken);
        if (Refusal(access) is { } refusal)
        {
            return refusal;
        }

        if (file.Length == 0 || file.Length > MaxDocumentBytes)
        {
            return BadRequest(new { message = "Het document moet tussen 1 byte en 10 MB groot zijn." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/png",
        };
        await using var stream = file.OpenReadStream();
        var updated = await _service.AttachFileAsync(
            id, Path.GetFileName(file.FileName), contentType, stream, cancellationToken, access.Scope);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("api/order-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage, PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken cancellationToken)
    {
        var access = await _access.AuthorizeAsync(id, OrderDocumentOperation.Download, cancellationToken);
        if (Refusal(access) is { } refusal)
        {
            return refusal;
        }

        var file = await _service.OpenFileAsync(id, cancellationToken, access.Scope);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    [HttpDelete("api/order-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.DossiersManage)]
    public async Task<IActionResult> RemoveFile(Guid id, CancellationToken cancellationToken)
    {
        var access = await _access.AuthorizeAsync(id, OrderDocumentOperation.Write, cancellationToken);
        if (Refusal(access) is { } refusal)
        {
            return refusal;
        }

        return await _service.RemoveFileAsync(id, cancellationToken, access.Scope) ? NoContent() : NotFound();
    }

    /// <summary>Null = allowed. Forbidden mirrors the body <see cref="RequirePermissionAttribute"/> produces.</summary>
    private ActionResult? Refusal(OrderDocumentAccess access)
    {
        switch (access.Outcome)
        {
            case OrderDocumentAccessOutcome.Allowed:
                return null;
            case OrderDocumentAccessOutcome.NotFound:
                return NotFound();
            default:
                var forbidden = new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = $"Missing permission: {string.Join(" or ", access.RequiredPermissions)}",
                    Status = StatusCodes.Status403Forbidden,
                };
                forbidden.Extensions["code"] = Common.ErrorCodes.Forbidden;
                return new ObjectResult(forbidden)
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    ContentTypes = { "application/problem+json" },
                };
        }
    }
}
