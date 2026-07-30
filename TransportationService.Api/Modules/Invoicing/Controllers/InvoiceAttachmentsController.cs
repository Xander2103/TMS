using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Invoicing.Services;

namespace TransportationService.Api.Modules.Invoicing.Controllers;

[ApiController]
[Route("api/invoices/{invoiceId:guid}/attachments")]
public class InvoiceAttachmentsController : ControllerBase
{
    private const long MaxAttachmentBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".xml"] = "application/xml",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".csv"] = "text/csv",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    private readonly IInvoiceAttachmentService _service;

    public InvoiceAttachmentsController(IInvoiceAttachmentService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.InvoiceAttachmentsView, PermissionCodes.InvoiceAttachmentsManage)]
    public async Task<ActionResult<IReadOnlyList<InvoiceAttachmentDto>>> List(Guid invoiceId, CancellationToken cancellationToken)
    {
        var attachments = await _service.ListAsync(invoiceId, cancellationToken);
        return attachments is null ? NotFound() : Ok(attachments);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.InvoiceAttachmentsManage)]
    [RequestSizeLimit(MaxAttachmentBytes + 1024)]
    public async Task<ActionResult<InvoiceAttachmentDto>> Upload(Guid invoiceId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxAttachmentBytes)
        {
            return BadRequest(new { message = "De bijlage moet tussen 1 byte en 10 MB groot zijn." });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.TryGetValue(extension, out var contentType))
        {
            return BadRequest(new { message = "Alleen PDF-, XML-, XLSX-, XLS-, CSV-, JPG- en PNG-bestanden zijn toegestaan." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        await using var stream = file.OpenReadStream();
        var uploaded = await _service.UploadAsync(invoiceId, file.FileName, contentType, file.Length, stream, cancellationToken);
        return uploaded is null ? NotFound() : Ok(uploaded);
    }

    [HttpPut("{attachmentId:guid}")]
    [RequirePermission(PermissionCodes.InvoiceAttachmentsManage)]
    public async Task<ActionResult<InvoiceAttachmentDto>> Update(
        Guid invoiceId, Guid attachmentId, UpdateInvoiceAttachmentRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(invoiceId, attachmentId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("{attachmentId:guid}/content")]
    [RequirePermission(PermissionCodes.InvoiceAttachmentsView, PermissionCodes.InvoiceAttachmentsManage)]
    public async Task<IActionResult> Download(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await _service.OpenAsync(invoiceId, attachmentId, cancellationToken);
        if (attachment is not { } found)
        {
            return NotFound();
        }

        return File(found.Content, found.ContentType, found.FileName);
    }

    [HttpDelete("{attachmentId:guid}")]
    [RequirePermission(PermissionCodes.InvoiceAttachmentsManage)]
    public async Task<IActionResult> Delete(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(invoiceId, attachmentId, cancellationToken) ? NoContent() : NotFound();
    }
}
