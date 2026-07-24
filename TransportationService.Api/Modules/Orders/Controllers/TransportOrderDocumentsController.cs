using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>Order documents: metadata nested under the order, file ops on the flat route.</summary>
[ApiController]
public class TransportOrderDocumentsController : ControllerBase
{
    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly ITransportOrderDocumentService _service;

    public TransportOrderDocumentsController(ITransportOrderDocumentService service)
    {
        _service = service;
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

    [HttpPut("api/order-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDocumentDto>> Update(
        Guid id, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/order-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("api/order-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<ActionResult<TransportOrderDocumentDto>> UploadFile(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxDocumentBytes)
        {
            return BadRequest(new { message = "Het document moet tussen 1 byte en 10 MB groot zijn." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan." });
        }

        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/png",
        };
        await using var stream = file.OpenReadStream();
        var updated = await _service.AttachFileAsync(id, Path.GetFileName(file.FileName), contentType, stream, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("api/order-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken cancellationToken)
    {
        var file = await _service.OpenFileAsync(id, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    [HttpDelete("api/order-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> RemoveFile(Guid id, CancellationToken cancellationToken) =>
        await _service.RemoveFileAsync(id, cancellationToken) ? NoContent() : NotFound();
}
