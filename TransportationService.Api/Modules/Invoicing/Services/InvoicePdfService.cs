using TransportationService.Api.Common.Money;
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
/// Assembles the printable PDF from the invoice's FROZEN seller/customer snapshot (ACC-H4):
/// once issued, nothing here reads the live Customer or LegalEntity any more, so later master
/// data changes never alter an issued document. A draft preview falls back to live data only
/// for fields a pre-remediation draft never snapshotted. The logo is the one live asset
/// (binary, not snapshotted). Never the structured Peppol document — presentation only.
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

        var isDraft = invoice.Status == InvoiceStatus.Draft;
        var issued = invoice.Status is InvoiceStatus.Sent or InvoiceStatus.Paid;

        // Live fallbacks exist for legacy rows only; an issued document never needs them, and a
        // draft gets its snapshot refreshed on every save.
        var liveCustomer = invoice.CustomerNameSnapshot is null && !issued
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == invoice.CustomerId)
                .Select(c => new { c.Name, c.LegalName, c.CompanyNumber, c.Street, c.HouseNumber, c.PostalCode, c.City, c.CountryCode })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var liveEntity = invoice.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Id == entityId)
                .Select(e => new { e.Bic, e.InvoiceFooter, e.LogoStorageKey, e.CompanyNumber, e.LegalName })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        byte[]? logoBytes = null;
        if (liveEntity?.LogoStorageKey is { Length: > 0 } logoKey)
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
        // H-06: a MIRRORED credit-note line carries the credited document's wording from the moment
        // it is copied and Send never re-derives it — so the preview must not re-derive it either.
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
                l.Quantity, l.UnitPrice, l.VatRatePercent, MoneyRounding.LineTotal(l.Quantity, l.UnitPrice),
                l.UnitLabel ?? UnEceUnitCodeMap.LabelFor(l.UnitCode)))
            .ToList();
        var subtotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        var vat = InvoiceTotals.VatTotal(invoice.Lines, invoice.CustomerVatTreatment);

        var creditedInvoiceNumber = invoice.CreditedInvoiceId is { } creditedId
            ? await _dbContext.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Id == creditedId)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var customerName = invoice.CustomerLegalNameSnapshot ?? invoice.CustomerNameSnapshot
                           ?? liveCustomer?.LegalName ?? liveCustomer?.Name ?? string.Empty;
        var customerAddress = invoice.CustomerNameSnapshot is not null
            ? InvoiceService.AddressLine(invoice.CustomerStreetSnapshot, invoice.CustomerHouseNumberSnapshot,
                invoice.CustomerPostalCodeSnapshot, invoice.CustomerCitySnapshot, invoice.CustomerCountryCodeSnapshot)
            : liveCustomer is null ? null
            : InvoiceService.AddressLine(liveCustomer.Street, liveCustomer.HouseNumber, liveCustomer.PostalCode, liveCustomer.City, liveCustomer.CountryCode);
        var strings = InvoicePdfStrings.For(invoice.LanguageCode);

        var snapshot = new InvoicePdfSnapshot(
            invoice.SellerLegalName ?? invoice.SellerName ?? liveEntity?.LegalName ?? string.Empty,
            invoice.SellerAddressLine,
            invoice.SellerVatNumber,
            invoice.SellerIban,
            // Frozen at issue; a legacy issued row (no snapshot) keeps its historic live reading.
            invoice.SellerBic ?? (issued && invoice.SellerName is not null && invoice.SellerLegalName is not null ? null : liveEntity?.Bic),
            invoice.SellerInvoiceFooter ?? (invoice.SellerLegalName is not null ? null : liveEntity?.InvoiceFooter),
            invoice.VatLegalText,
            logoBytes,
            invoice.DisplayNumber,
            invoice.InvoiceDate,
            invoice.DueDate,
            customerName,
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
            IsDraft: isDraft,
            SellerCompanyNumber: invoice.SellerCompanyNumber ?? (invoice.SellerLegalName is not null ? null : liveEntity?.CompanyNumber),
            CustomerCompanyNumber: invoice.CustomerCompanyNumberSnapshot ?? liveCustomer?.CompanyNumber,
            PaymentTermsText: invoice.Kind == InvoiceKind.CreditNote ? null : strings.PaymentTermsText(invoice.PaymentTermDays, invoice.DueDate),
            PaymentReference: invoice.PaymentReference ?? invoice.InvoiceNumber,
            CreditReason: invoice.CreditReason);

        var bytes = InvoicePdfRenderer.Render(snapshot);
        var prefix = invoice.Kind == InvoiceKind.CreditNote ? "creditnota" : "factuur";
        if (isDraft) prefix = "concept-" + prefix;
        var fileName = $"{prefix}-{invoice.DisplayNumber.Replace('/', '-')}.pdf";
        return (bytes, fileName);
    }
}
