using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

public interface IPeppolInvoiceService
{
    /// <summary>Full Dutch readiness checklist for sending one invoice via Peppol. Null = unknown invoice.</summary>
    Task<PeppolInvoiceValidationResultDto?> ValidateAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Structured document summary (parties, totals per VAT category). Null = unknown invoice.</summary>
    Task<PeppolInvoicePreviewDto?> PreviewAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>The rendered UBL XML; null when the invoice is unknown, error result when invalid.</summary>
    Task<(string? Xml, PeppolInvoiceValidationResultDto? Failure)?> GetXmlAsync(Guid invoiceId, CancellationToken cancellationToken);
}

/// <summary>
/// Validation catalog + preview + XML rendering for one invoice. Every rule produces a
/// Dutch message the boekhouding can act on; the builder itself is never reached while a
/// blocking issue exists. Reads happen snapshot-first (frozen invoice facts win over live
/// master data), mirroring what the generated document will contain.
/// </summary>
public class PeppolInvoiceService : IPeppolInvoiceService
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;

    public PeppolInvoiceService(TransportationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private sealed record InvoiceGraph(
        Invoice Invoice, List<InvoiceLine> Lines, Customer Customer, LegalEntity? Seller,
        PeppolSettings? Settings, string? CreditedInvoiceNumber);

    public async Task<PeppolInvoiceValidationResultDto?> ValidateAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(invoiceId, cancellationToken);
        return graph is null ? null : Validate(graph);
    }

    public async Task<PeppolInvoicePreviewDto?> PreviewAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(invoiceId, cancellationToken);
        if (graph is null)
        {
            return null;
        }

        var treatment = ParseTreatment(graph.Invoice);
        var lines = graph.Lines.OrderBy(l => l.Sequence).ToList();
        var groups = lines
            .GroupBy(l => (Category: l.VatCategoryCode ?? VatTreatmentCatalog.ResolveVatCategory(treatment, l.VatRatePercent).Code, l.VatRatePercent))
            .OrderBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.VatRatePercent)
            .Select(g =>
            {
                var taxable = Math.Round(g.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), 2);
                return new PeppolPreviewVatGroupDto(
                    g.Key.Category, g.Key.VatRatePercent, taxable,
                    Math.Round(taxable * g.Key.VatRatePercent / 100m, 2));
            })
            .ToList();
        var subtotal = Math.Round(lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), 2);
        var vat = groups.Sum(g => g.VatAmount);

        return new PeppolInvoicePreviewDto(
            graph.Invoice.InvoiceNumber,
            graph.Invoice.Kind.ToString(),
            graph.Invoice.InvoiceDate,
            graph.Invoice.Currency,
            new PeppolPreviewPartyDto(
                graph.Invoice.SellerName ?? graph.Seller?.LegalName ?? string.Empty,
                graph.Invoice.SellerVatNumber ?? graph.Seller?.VatNumber,
                Participant(graph.Seller?.PeppolScheme, graph.Seller?.PeppolId)),
            new PeppolPreviewPartyDto(
                graph.Customer.LegalName ?? graph.Customer.Name,
                graph.Invoice.CustomerVatNumberSnapshot ?? graph.Customer.VatNumber,
                Participant(graph.Customer.PeppolScheme, graph.Customer.PeppolId)),
            lines.Select(l => new PeppolPreviewLineDto(
                l.Sequence, l.Description, l.Quantity, l.UnitCode, l.UnitPrice,
                Math.Round(l.Quantity * l.UnitPrice, 2),
                l.VatCategoryCode ?? VatTreatmentCatalog.ResolveVatCategory(treatment, l.VatRatePercent).Code,
                l.VatRatePercent)).ToList(),
            groups, subtotal, vat, subtotal + vat,
            FirstNonEmpty(graph.Customer.BuyerReference, graph.Invoice.PurchaseOrderNumber, graph.Invoice.InvoiceNumber),
            graph.Invoice.PurchaseOrderNumber,
            graph.CreditedInvoiceNumber);
    }

    public async Task<(string? Xml, PeppolInvoiceValidationResultDto? Failure)?> GetXmlAsync(
        Guid invoiceId, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(invoiceId, cancellationToken);
        if (graph is null)
        {
            return null;
        }

        var validation = Validate(graph);
        if (!validation.IsValid || graph.Seller is null)
        {
            return (null, validation);
        }

        var xml = UblDocumentBuilder.Build(new UblDocumentBuilder.UblDocumentInput(
            graph.Invoice, graph.Lines, graph.Customer, graph.Seller,
            graph.CreditedInvoiceNumber, graph.Settings?.DefaultInvoiceNote));
        return (xml, null);
    }

    private async Task<InvoiceGraph?> LoadAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == invoice.CustomerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var seller = invoice.LegalEntityId is { } entityId
            ? await _db.LegalEntities.AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == entityId, cancellationToken)
            : null;
        var settings = seller is null
            ? null
            : await _db.PeppolSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.LegalEntityId == seller.Id, cancellationToken);
        var creditedNumber = invoice.CreditedInvoiceId is { } creditedId
            ? await _db.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Id == creditedId)
                .Select(i => i.InvoiceNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new InvoiceGraph(
            invoice, invoice.Lines.Where(l => !l.IsDeleted).ToList(), customer, seller, settings, creditedNumber);
    }

    private static PeppolInvoiceValidationResultDto Validate(InvoiceGraph graph)
    {
        var issues = new List<PeppolValidationIssueDto>();
        void Add(string code, string message) => issues.Add(new PeppolValidationIssueDto(code, message));

        var invoice = graph.Invoice;
        var treatment = ParseTreatment(invoice);

        // --- Own company / configuration ---
        if (graph.Seller is null)
        {
            Add("company.missing", "Deze factuur heeft geen facturerende entiteit; kies eerst een eigen bedrijf.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(graph.Seller.PeppolId) || string.IsNullOrWhiteSpace(graph.Seller.PeppolScheme))
            {
                Add("company.peppol_identity", $"Het eigen bedrijf '{graph.Seller.LegalName}' heeft geen Peppol-ID en -schema.");
            }

            if (string.IsNullOrWhiteSpace(invoice.SellerVatNumber ?? graph.Seller.VatNumber))
            {
                Add("company.vat", $"Het eigen bedrijf '{graph.Seller.LegalName}' heeft geen BTW-nummer.");
            }

            if (invoice.Kind == InvoiceKind.Invoice && string.IsNullOrWhiteSpace(invoice.SellerIban ?? graph.Seller.Iban))
            {
                Add("company.iban", $"Het eigen bedrijf '{graph.Seller.LegalName}' heeft geen IBAN voor de betaalgegevens.");
            }

            if (string.IsNullOrWhiteSpace(graph.Seller.CountryCode))
            {
                Add("company.address", $"Het eigen bedrijf '{graph.Seller.LegalName}' heeft geen land in het adres.");
            }

            if (graph.Settings is not { Enabled: true })
            {
                Add("company.peppol_disabled", $"Peppol is niet ingeschakeld voor '{graph.Seller.LegalName}' (Instellingen → Peppol).");
            }
        }

        // --- Customer ---
        if (string.IsNullOrWhiteSpace(graph.Customer.PeppolId) || string.IsNullOrWhiteSpace(graph.Customer.PeppolScheme))
        {
            Add("customer.peppol_identity", $"Klant '{graph.Customer.Name}' heeft geen Peppol-ID en -schema.");
        }
        else if (graph.Customer.PeppolValidationStatus == CustomerPeppolValidationStatus.NotFound)
        {
            Add("customer.not_found", $"Klant '{graph.Customer.Name}' werd bij de laatste controle niet gevonden in het Peppol-netwerk.");
        }

        if (!graph.Customer.PeppolEnabled)
        {
            Add("customer.peppol_disabled", $"Peppol is niet ingeschakeld voor klant '{graph.Customer.Name}' (fiscale gegevens).");
        }

        if (VatTreatmentCatalog.Resolve(treatment).RequiresVatNumber
            && string.IsNullOrWhiteSpace(invoice.CustomerVatNumberSnapshot ?? graph.Customer.VatNumber))
        {
            Add("customer.vat", "Deze btw-regeling vereist een BTW-nummer van de klant.");
        }

        if (string.IsNullOrWhiteSpace(graph.Customer.CountryCode))
        {
            Add("customer.address", $"Klant '{graph.Customer.Name}' heeft geen land in het adres.");
        }

        // --- Invoice state ---
        if (invoice.Status == InvoiceStatus.Draft)
        {
            Add("invoice.not_finalized", "De factuur is nog een concept; verzend haar eerst (definitief nummer en bevroren gegevens).");
        }
        else if (invoice.Status == InvoiceStatus.Cancelled)
        {
            Add("invoice.cancelled", "Een geannuleerde factuur kan niet via Peppol verstuurd worden.");
        }

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            Add("invoice.number", "De factuur heeft geen factuurnummer.");
        }

        if (invoice.Currency is not { Length: 3 } || !invoice.Currency.All(char.IsAsciiLetter))
        {
            Add("invoice.currency", "De factuur heeft geen geldige valutacode (bv. EUR).");
        }

        if (string.IsNullOrWhiteSpace(invoice.CustomerVatTreatment))
        {
            Add("invoice.vat_treatment", "De btw-regeling van de klant ontbreekt op de factuur.");
        }

        if (graph.Customer.PurchaseOrderPolicy == PurchaseOrderPolicy.Required
            && string.IsNullOrWhiteSpace(invoice.PurchaseOrderNumber))
        {
            Add("invoice.po_required", "Deze klant vereist een PO-nummer; vul het aan voor verzending via Peppol.");
        }

        // --- Lines & totals ---
        if (graph.Lines.Count == 0)
        {
            Add("lines.empty", "De factuur heeft geen lijnen.");
        }

        foreach (var line in graph.Lines.OrderBy(l => l.Sequence))
        {
            if (line.Quantity <= 0)
            {
                Add("lines.quantity", $"Lijn {line.Sequence}: de hoeveelheid moet groter zijn dan nul.");
            }

            if (string.IsNullOrWhiteSpace(line.UnitCode))
            {
                Add("lines.unit", $"Lijn {line.Sequence}: er is geen eenheidscode (bv. C62).");
            }

            if (line.VatRatePercent is < 0 or > 100)
            {
                Add("lines.vat_rate", $"Lijn {line.Sequence}: het btw-tarief is ongeldig.");
            }

            // BR-27: negative unit prices are forbidden; discounts belong in reduced lines or a credit note.
            if (line.UnitPrice < 0)
            {
                Add("lines.negative_price",
                    $"Lijn {line.Sequence}: een negatieve prijs is niet toegestaan in Peppol; verlaag de lijnen of maak een creditnota.");
            }

            // BR-S-05 / BR-AE-05 / BR-IC-05 / BR-G-05 / BR-E-05: category and rate must agree.
            var category = line.VatCategoryCode ?? VatTreatmentCatalog.ResolveVatCategory(treatment, line.VatRatePercent).Code;
            if (category is "AE" or "K" or "G" or "E" && line.VatRatePercent != 0m)
            {
                Add("lines.category_rate",
                    $"Lijn {line.Sequence}: btw-categorie {category} vereist een tarief van 0%, maar de lijn heeft {line.VatRatePercent}%.");
            }
            else if (category == "S" && line.VatRatePercent <= 0m)
            {
                Add("lines.category_rate",
                    $"Lijn {line.Sequence}: btw-categorie S vereist een tarief boven 0%; gebruik een vrijstellingsregeling voor 0%-lijnen.");
            }
        }

        var total = Math.Round(graph.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), 2);
        if (invoice.Kind == InvoiceKind.Invoice && total < 0)
        {
            Add("totals.negative", "Het factuurtotaal is negatief; maak een creditnota in plaats van een negatieve factuur.");
        }

        if (invoice.Kind == InvoiceKind.CreditNote && total < 0)
        {
            Add("totals.negative_credit", "Een creditnota draagt positieve bedragen; het teken zit in het documenttype.");
        }

        // --- Accounting completeness (only meaningful once frozen) ---
        if (invoice.Status is InvoiceStatus.Sent or InvoiceStatus.Paid)
        {
            var missingLedger = graph.Lines.Count(l => l.LedgerAccountNumberSnapshot is null);
            if (missingLedger > 0)
            {
                Add("accounting.ledger", $"{missingLedger} lijn(en) missen een bevroren grootboekrekening; vul de boekhoudsnapshot aan.");
            }
        }

        return new PeppolInvoiceValidationResultDto(issues.Count == 0, issues);
    }

    private static VatTreatment ParseTreatment(Invoice invoice) =>
        Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed) ? parsed : VatTreatment.DomesticVat;

    private static string? Participant(string? scheme, string? id) =>
        string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(id) ? null : $"{scheme}:{id}";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
