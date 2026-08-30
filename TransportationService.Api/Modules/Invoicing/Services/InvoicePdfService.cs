using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public interface IInvoicePdfService
{
    /// <summary>Renders the invoice PDF; null when the invoice doesn't exist in this tenant.
    /// Presentation only — every invoice status is renderable (Draft included) for internal use.</summary>
    Task<(byte[] Bytes, string FileName)?> RenderAsync(Guid invoiceId, CancellationToken cancellationToken);
}

/// <summary>
/// Assembles the frozen seller/customer snapshot already on <see cref="Invoice"/> plus the
/// (live, never snapshotted) legal-entity footer/logo/banking details into a printable PDF via
/// <see cref="InvoicePdfRenderer"/>. Never the structured Peppol document — presentation only.
/// </summary>
public class InvoicePdfService : IInvoicePdfService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorageService _fileStorage;

    public InvoicePdfService(TransportationDbContext dbContext, ITenantContext tenantContext, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
    }

    public async Task<(byte[] Bytes, string FileName)?> RenderAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var invoice = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Lines.Where(l => !l.IsDeleted))
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == invoice.CustomerId)
            .Select(c => new { c.Name, c.Street, c.HouseNumber, c.PostalCode, c.City, c.CountryCode })
            .FirstOrDefaultAsync(cancellationToken);

        LegalEntitySnapshot? legalEntity = invoice.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Id == entityId)
                .Select(e => new LegalEntitySnapshot(e.Bic, e.InvoiceFooter, e.LogoStorageKey))
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        byte[]? logoBytes = null;
        if (legalEntity?.LogoStorageKey is { Length: > 0 } logoKey)
        {
            try
            {
                await using var stream = await _fileStorage.OpenReadAsync(logoKey, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                logoBytes = buffer.ToArray();
            }
            catch (IOException)
            {
                // Missing/unreadable logo file (incl. FileNotFoundException, an IOException
                // subtype) never blocks invoice PDF generation.
            }
        }

        // A draft renders the description the customer WILL read (same rule Send freezes with);
        // after Send the line text is already frozen and the sales code is not consulted.
        var isDraft = invoice.Status == InvoiceStatus.Draft;
        // H-06: a MIRRORED credit-note line carries the credited document's wording from the moment
        // it is copied and Send never re-derives it — so the preview must not re-derive it either.
        // A line typed on the draft credit note itself is not mirrored and follows the normal rule.
        var liveLines = invoice.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Sequence).ToList();
        bool ResolveLive(Entities.InvoiceLine line) => isDraft && !InvoiceLineMirror.IsMirrored(invoice, line);
        var codeIds = isDraft
            ? liveLines.Where(l => ResolveLive(l) && l.SalesCategoryId is not null)
                .Select(l => l.SalesCategoryId!.Value).Distinct().ToList()
            : [];
        var salesCodes = codeIds.Count == 0
            ? new Dictionary<Guid, Modules.Accounting.Entities.SalesCategory>()
            : await _dbContext.SalesCategories.AsNoTracking()
                .Where(c => c.TenantId == tenantId && codeIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        var lines = liveLines
            .Select(l => new InvoicePdfLine(
                ResolveLive(l)
                    ? InvoiceLineDescriptions.CustomerFacing(
                        l.Description,
                        l.SalesCategoryId is { } codeId ? salesCodes.GetValueOrDefault(codeId) : null,
                        invoice.LanguageCode)
                    : l.Description,
                l.Quantity, l.UnitPrice, l.VatRatePercent, Math.Round(l.Quantity * l.UnitPrice, 2)))
            .ToList();
        var subtotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        var vat = InvoiceTotals.VatTotal(invoice.Lines, invoice.CustomerVatTreatment);

        var creditedInvoiceNumber = invoice.CreditedInvoiceId is { } creditedId
            ? await _dbContext.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Id == creditedId)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var customerAddress = customer is null
            ? null
            : string.Join(", ", new[]
            {
                string.Join(' ', new[] { customer.Street, customer.HouseNumber }.Where(s => !string.IsNullOrWhiteSpace(s))),
                string.Join(' ', new[] { customer.PostalCode, customer.City }.Where(s => !string.IsNullOrWhiteSpace(s))),
                customer.CountryCode,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var snapshot = new InvoicePdfSnapshot(
            invoice.SellerName ?? string.Empty,
            invoice.SellerAddressLine,
            invoice.SellerVatNumber,
            invoice.SellerIban,
            legalEntity?.Bic,
            legalEntity?.InvoiceFooter,
            invoice.VatLegalText,
            logoBytes,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.DueDate,
            customer?.Name ?? invoice.SellerName ?? string.Empty,
            customerAddress,
            invoice.CustomerVatNumberSnapshot,
            invoice.PurchaseOrderNumber,
            lines,
            subtotal,
            vat,
            subtotal + vat,
            invoice.Currency,
            invoice.Notes,
            invoice.Kind == InvoiceKind.CreditNote,
            creditedInvoiceNumber,
            invoice.LanguageCode,
            IsDraft: isDraft);

        var bytes = InvoicePdfRenderer.Render(snapshot);
        var prefix = invoice.Kind == InvoiceKind.CreditNote ? "creditnota" : "factuur";
        if (isDraft) prefix = "concept-" + prefix;
        var fileName = $"{prefix}-{invoice.InvoiceNumber.Replace('/', '-')}.pdf";
        return (bytes, fileName);
    }

    private sealed record LegalEntitySnapshot(string? Bic, string? InvoiceFooter, string? LogoStorageKey);
}
