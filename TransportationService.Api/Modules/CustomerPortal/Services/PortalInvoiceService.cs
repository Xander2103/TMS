using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface IPortalInvoiceService
{
    Task<PortalResult<IReadOnlyList<PortalInvoiceListItemDto>>> ListMyInvoicesAsync(CancellationToken cancellationToken);
    Task<PortalResult<PortalInvoiceDetailDto>> GetMyInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<PortalResult<PortalFileDto>> GetInvoicePdfAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<PortalResult<PortalFileDto>> GetInvoiceAttachmentAsync(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Customer-facing invoice list/detail/PDF/attachments — own customer only, Draft invoices
/// never surface (a Draft is an internal working document, not yet a real invoice to the
/// customer). PeppolStatus shows the newest transmission's progress; internal failure
/// states are deliberately hidden from customers.
/// </summary>
public class PortalInvoiceService : IPortalInvoiceService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoicePdfService _pdfService;
    private readonly IInvoiceAttachmentService _attachmentService;

    public PortalInvoiceService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext,
        IInvoiceService invoiceService, IInvoicePdfService pdfService, IInvoiceAttachmentService attachmentService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _invoiceService = invoiceService;
        _pdfService = pdfService;
        _attachmentService = attachmentService;
    }

    /// <summary>Dutch label of the newest non-cancelled Peppol transmission per invoice; null = none.</summary>
    private async Task<Dictionary<Guid, string?>> LatestPeppolStatusesAsync(
        IReadOnlyList<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
        {
            return [];
        }

        var transmissions = await _dbContext.PeppolTransmissions.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && invoiceIds.Contains(t.InvoiceId)
                        && t.Status != Modules.Peppol.Entities.PeppolTransmissionStatus.Cancelled)
            .Select(t => new { t.InvoiceId, t.Status, t.PayloadVersion })
            .ToListAsync(cancellationToken);
        return transmissions
            .GroupBy(t => t.InvoiceId)
            .ToDictionary(g => g.Key, g =>
            {
                var latest = g.OrderByDescending(t => t.PayloadVersion).First().Status;
                // Internal delivery failures are an internal matter; customers only see
                // progress and delivery, never Failed/Rejected.
                return latest is Modules.Peppol.Entities.PeppolTransmissionStatus.Failed
                    or Modules.Peppol.Entities.PeppolTransmissionStatus.Rejected
                    ? null
                    : (string?)latest.ToString();
            });
    }

    private async Task<Guid?> MyCustomerIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.CustomerId != null)
            .Select(u => u.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PortalResult<IReadOnlyList<PortalInvoiceListItemDto>>> ListMyInvoicesAsync(CancellationToken cancellationToken)
    {
        var customerId = await MyCustomerIdAsync(cancellationToken);
        if (customerId is null)
        {
            return PortalResult<IReadOnlyList<PortalInvoiceListItemDto>>.NoCustomerLink();
        }

        var page = await _invoiceService.SearchAsync(null, null, customerId, PageRequest.Of(1, 200), cancellationToken);
        var visible = page.Items.Where(i => i.Status != InvoiceStatus.Draft).ToList();
        var peppolStatuses = await LatestPeppolStatusesAsync(visible.Select(i => i.Id).ToList(), cancellationToken);
        var items = visible
            .Select(i => new PortalInvoiceListItemDto(
                i.Id, i.InvoiceNumber, i.InvoiceDate, i.DueDate, i.Status, i.Total, i.Currency,
                peppolStatuses.GetValueOrDefault(i.Id), i.Kind.ToString()))
            .ToList();
        return PortalResult<IReadOnlyList<PortalInvoiceListItemDto>>.Success(items);
    }

    public async Task<PortalResult<PortalInvoiceDetailDto>> GetMyInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var customerId = await MyCustomerIdAsync(cancellationToken);
        if (customerId is null)
        {
            return PortalResult<PortalInvoiceDetailDto>.NoCustomerLink();
        }

        var invoice = await _invoiceService.GetByIdAsync(invoiceId, cancellationToken);
        if (invoice is null || invoice.CustomerId != customerId || invoice.Status == InvoiceStatus.Draft)
        {
            return PortalResult<PortalInvoiceDetailDto>.NotFound();
        }

        var attachments = await _attachmentService.ListAsync(invoiceId, cancellationToken) ?? [];
        var visibleAttachments = attachments
            .Where(a => a.IncludeWhenSending)
            .Select(a => new PortalInvoiceAttachmentDto(a.Id, a.FileName, a.SizeBytes))
            .ToList();

        return PortalResult<PortalInvoiceDetailDto>.Success(new PortalInvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DueDate, invoice.Status, invoice.Currency,
            invoice.PurchaseOrderNumber,
            invoice.Lines.Select(l => new PortalInvoiceLineDto(l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent, l.LineTotal)).ToList(),
            invoice.Subtotal, invoice.VatAmount, invoice.Total, visibleAttachments,
            (await LatestPeppolStatusesAsync([invoice.Id], cancellationToken)).GetValueOrDefault(invoice.Id),
            invoice.Kind.ToString()));
    }

    public async Task<PortalResult<PortalFileDto>> GetInvoicePdfAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var customerId = await MyCustomerIdAsync(cancellationToken);
        if (customerId is null)
        {
            return PortalResult<PortalFileDto>.NoCustomerLink();
        }

        var invoice = await _invoiceService.GetByIdAsync(invoiceId, cancellationToken);
        if (invoice is null || invoice.CustomerId != customerId || invoice.Status == InvoiceStatus.Draft)
        {
            return PortalResult<PortalFileDto>.NotFound();
        }

        var pdf = await _pdfService.RenderAsync(invoiceId, cancellationToken);
        return pdf is null
            ? PortalResult<PortalFileDto>.NotFound()
            : PortalResult<PortalFileDto>.Success(new PortalFileDto(pdf.Value.Bytes, pdf.Value.FileName, "application/pdf"));
    }

    public async Task<PortalResult<PortalFileDto>> GetInvoiceAttachmentAsync(
        Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var customerId = await MyCustomerIdAsync(cancellationToken);
        if (customerId is null)
        {
            return PortalResult<PortalFileDto>.NoCustomerLink();
        }

        var invoice = await _invoiceService.GetByIdAsync(invoiceId, cancellationToken);
        if (invoice is null || invoice.CustomerId != customerId || invoice.Status == InvoiceStatus.Draft)
        {
            return PortalResult<PortalFileDto>.NotFound();
        }

        var attachments = await _attachmentService.ListAsync(invoiceId, cancellationToken) ?? [];
        var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null || !attachment.IncludeWhenSending)
        {
            return PortalResult<PortalFileDto>.NotFound();
        }

        var opened = await _attachmentService.OpenAsync(invoiceId, attachmentId, cancellationToken);
        if (opened is null)
        {
            return PortalResult<PortalFileDto>.NotFound();
        }

        await using var stream = opened.Value.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return PortalResult<PortalFileDto>.Success(new PortalFileDto(buffer.ToArray(), opened.Value.FileName, opened.Value.ContentType));
    }
}
