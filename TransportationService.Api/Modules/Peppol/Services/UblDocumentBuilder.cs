using System.Globalization;
using System.Xml.Linq;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>
/// Pure, deterministic UBL 2.1 builder for Peppol BIS Billing 3.0 invoices and credit notes.
/// Two builds over the same inputs are byte-equal (invariant culture, stable ordering, no
/// clock reads) — the transmission payload hash relies on this. All fiscal facts come from
/// the invoice's own snapshots; nothing is re-read from live master data here.
/// </summary>
public static class UblDocumentBuilder
{
    public const string CustomizationId = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0";
    public const string ProfileId = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace CreditNoteNs = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>Everything the builder needs, pre-resolved by the caller (no DbContext here).</summary>
    public sealed record UblDocumentInput(
        Invoice Invoice,
        IReadOnlyList<InvoiceLine> Lines,
        Customer Customer,
        LegalEntity Seller,
        string? CreditedInvoiceNumber,
        string? DefaultInvoiceNote);

    public static string Build(UblDocumentInput input)
    {
        var invoice = input.Invoice;
        var isCreditNote = invoice.Kind == InvoiceKind.CreditNote;

        // Defensive: the validation catalog gates these upstream, but a payload with empty
        // endpoint ids or countries must never be produced (phase-13 callers included).
        if (string.IsNullOrWhiteSpace(input.Seller.PeppolId) || string.IsNullOrWhiteSpace(input.Seller.PeppolScheme))
        {
            throw new InvalidOperationException("UBL build refused: seller has no Peppol identity.");
        }

        if (string.IsNullOrWhiteSpace(input.Customer.PeppolId) || string.IsNullOrWhiteSpace(input.Customer.PeppolScheme))
        {
            throw new InvalidOperationException("UBL build refused: customer has no Peppol identity.");
        }

        if (string.IsNullOrWhiteSpace(input.Seller.CountryCode) || string.IsNullOrWhiteSpace(input.Customer.CountryCode))
        {
            throw new InvalidOperationException("UBL build refused: seller or customer has no country code.");
        }
        var root = isCreditNote ? CreditNoteNs + "CreditNote" : InvoiceNs + "Invoice";
        var treatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed)
            ? parsed
            : VatTreatment.DomesticVat;

        var lines = input.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Sequence).ToList();

        // Tax subtotals grouped by (category, rate) with per-group rounding; the tax total is
        // the sum of the ROUNDED group amounts so document totals always reconcile.
        var groups = lines
            .GroupBy(l => (Category: EffectiveCategory(l, treatment), l.VatRatePercent))
            .OrderBy(g => g.Key.Category, StringComparer.Ordinal)
            .ThenBy(g => g.Key.VatRatePercent)
            .Select(g =>
            {
                var taxable = Round2(g.Sum(l => LineExtension(l)));
                return new
                {
                    g.Key.Category,
                    Rate = g.Key.VatRatePercent,
                    Taxable = taxable,
                    Tax = Round2(taxable * g.Key.VatRatePercent / 100m),
                };
            })
            .ToList();

        var lineExtensionTotal = Round2(lines.Sum(LineExtension));
        var taxTotal = groups.Sum(g => g.Tax);
        var taxInclusive = lineExtensionTotal + taxTotal;

        var buyerReference = FirstNonEmpty(input.Customer.BuyerReference, invoice.PurchaseOrderNumber, invoice.InvoiceNumber);
        var note = FirstNonEmpty(invoice.Notes, input.DefaultInvoiceNote);

        var document = new XElement(root,
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XElement(Cbc + "CustomizationID", CustomizationId),
            new XElement(Cbc + "ProfileID", ProfileId),
            new XElement(Cbc + "ID", invoice.InvoiceNumber),
            new XElement(Cbc + "IssueDate", invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            isCreditNote ? null : new XElement(Cbc + "DueDate", invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Cbc + (isCreditNote ? "CreditNoteTypeCode" : "InvoiceTypeCode"), isCreditNote ? "381" : "380"),
            note is null ? null : new XElement(Cbc + "Note", note),
            new XElement(Cbc + "DocumentCurrencyCode", invoice.Currency),
            new XElement(Cbc + "BuyerReference", buyerReference),
            // BG-14 invoicing period: mandatory for intra-community (K) documents (BR-IC-11);
            // harmless and informative for every other category, so always emitted.
            InvoicePeriodElement(invoice),
            string.IsNullOrWhiteSpace(invoice.PurchaseOrderNumber)
                ? null
                : new XElement(Cac + "OrderReference", new XElement(Cbc + "ID", invoice.PurchaseOrderNumber)),
            isCreditNote && input.CreditedInvoiceNumber is not null
                ? new XElement(Cac + "BillingReference",
                    new XElement(Cac + "InvoiceDocumentReference",
                        new XElement(Cbc + "ID", input.CreditedInvoiceNumber)))
                : null,
            SellerParty(input.Seller, invoice),
            BuyerParty(input.Customer, invoice),
            PaymentMeans(invoice),
            TaxTotalElement(invoice, treatment, groups.Select(g => (g.Category, g.Rate, g.Taxable, g.Tax)).ToList(), taxTotal),
            new XElement(Cac + "LegalMonetaryTotal",
                Amount("LineExtensionAmount", lineExtensionTotal, invoice.Currency),
                Amount("TaxExclusiveAmount", lineExtensionTotal, invoice.Currency),
                Amount("TaxInclusiveAmount", taxInclusive, invoice.Currency),
                Amount("PayableAmount", taxInclusive, invoice.Currency)),
            lines.Select(l => LineElement(l, invoice, treatment, isCreditNote)));

        // Note: document-level allowances/charges (BG-20/BG-21) are deliberately absent — the
        // invoice model has no discount/charge concept; discounts are ordinary (positive) lines.
        // Serialize via XmlWriter so the XML declaration survives (XDocument.ToString drops it).
        var xmlDocument = new XDocument(new XDeclaration("1.0", "utf-8", null), document);
        var builder = new System.Text.StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(builder, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Encoding = System.Text.Encoding.UTF8,
            OmitXmlDeclaration = false,
        }))
        {
            xmlDocument.Save(writer);
        }

        // StringBuilder-backed writers stamp encoding="utf-16"; the payload is stored/sent as
        // UTF-8 bytes, so declare that.
        return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    /// <summary>BG-14: the invoice period (boekmaand) as start/end of that calendar month.</summary>
    private static XElement InvoicePeriodElement(Invoice invoice)
    {
        // Pre-wave rows may miss the period columns; the invoice date's month is the documented default.
        var (year, month) = invoice.InvoicePeriodYear >= 1 && invoice.InvoicePeriodMonth is >= 1 and <= 12
            ? (invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth)
            : (invoice.InvoiceDate.Year, invoice.InvoiceDate.Month);
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return new XElement(Cac + "InvoicePeriod",
            new XElement(Cbc + "StartDate", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "EndDate", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    private static XElement SellerParty(LegalEntity seller, Invoice invoice) =>
        new(Cac + "AccountingSupplierParty",
            new XElement(Cac + "Party",
                new XElement(Cbc + "EndpointID", new XAttribute("schemeID", seller.PeppolScheme ?? string.Empty), seller.PeppolId ?? string.Empty),
                new XElement(Cac + "PartyName",
                    new XElement(Cbc + "Name", invoice.SellerName ?? seller.TradingName ?? seller.LegalName)),
                Address(seller.Street, seller.HouseNumber, seller.City, seller.PostalCode, seller.CountryCode ?? "BE"),
                TaxParty(invoice.SellerVatNumber ?? seller.VatNumber),
                new XElement(Cac + "PartyLegalEntity",
                    new XElement(Cbc + "RegistrationName", seller.LegalName))));

    private static XElement BuyerParty(Customer customer, Invoice invoice) =>
        new(Cac + "AccountingCustomerParty",
            new XElement(Cac + "Party",
                new XElement(Cbc + "EndpointID", new XAttribute("schemeID", customer.PeppolScheme ?? string.Empty), customer.PeppolId ?? string.Empty),
                new XElement(Cac + "PartyName",
                    new XElement(Cbc + "Name", customer.LegalName ?? customer.Name)),
                Address(customer.Street, customer.HouseNumber, customer.City, customer.PostalCode, customer.CountryCode ?? "BE"),
                TaxParty(invoice.CustomerVatNumberSnapshot ?? customer.VatNumber),
                new XElement(Cac + "PartyLegalEntity",
                    new XElement(Cbc + "RegistrationName", customer.LegalName ?? customer.Name))));

    private static XElement Address(string? street, string? houseNumber, string? city, string? postalCode, string countryCode)
    {
        var streetLine = string.Join(' ', new[] { street, houseNumber }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return new XElement(Cac + "PostalAddress",
            string.IsNullOrWhiteSpace(streetLine) ? null : new XElement(Cbc + "StreetName", streetLine),
            string.IsNullOrWhiteSpace(city) ? null : new XElement(Cbc + "CityName", city),
            string.IsNullOrWhiteSpace(postalCode) ? null : new XElement(Cbc + "PostalZone", postalCode),
            new XElement(Cac + "Country",
                new XElement(Cbc + "IdentificationCode", countryCode)));
    }

    private static XElement? TaxParty(string? vatNumber) =>
        string.IsNullOrWhiteSpace(vatNumber)
            ? null
            : new XElement(Cac + "PartyTaxScheme",
                new XElement(Cbc + "CompanyID", vatNumber),
                new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")));

    private static XElement? PaymentMeans(Invoice invoice)
    {
        // Credit notes carry no payment instruction; invoices pay to the frozen seller IBAN.
        if (invoice.Kind == InvoiceKind.CreditNote || string.IsNullOrWhiteSpace(invoice.SellerIban))
        {
            return null;
        }

        return new XElement(Cac + "PaymentMeans",
            new XElement(Cbc + "PaymentMeansCode", "30"),
            string.IsNullOrWhiteSpace(invoice.PaymentReference)
                ? null
                : new XElement(Cbc + "PaymentID", invoice.PaymentReference),
            new XElement(Cac + "PayeeFinancialAccount",
                new XElement(Cbc + "ID", invoice.SellerIban)));
    }

    private static XElement TaxTotalElement(
        Invoice invoice, VatTreatment treatment,
        IReadOnlyList<(string Category, decimal Rate, decimal Taxable, decimal Tax)> groups, decimal taxTotal) =>
        new(Cac + "TaxTotal",
            Amount("TaxAmount", taxTotal, invoice.Currency),
            groups.Select(g =>
            {
                var category = VatTreatmentCatalog.ResolveVatCategory(treatment, g.Rate);
                var exemptionApplies = g.Category is "AE" or "K" or "G" or "E";
                return new XElement(Cac + "TaxSubtotal",
                    Amount("TaxableAmount", g.Taxable, invoice.Currency),
                    Amount("TaxAmount", g.Tax, invoice.Currency),
                    new XElement(Cac + "TaxCategory",
                        new XElement(Cbc + "ID", g.Category),
                        new XElement(Cbc + "Percent", Percent(g.Rate)),
                        exemptionApplies && category.ExemptionReasonCode is not null
                            ? new XElement(Cbc + "TaxExemptionReasonCode", category.ExemptionReasonCode)
                            : null,
                        exemptionApplies && (category.ExemptionReasonText ?? invoice.VatLegalText) is { } reason
                            ? new XElement(Cbc + "TaxExemptionReason", reason)
                            : null,
                        new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))));
            }));

    private static XElement LineElement(InvoiceLine line, Invoice invoice, VatTreatment treatment, bool isCreditNote) =>
        new(Cac + (isCreditNote ? "CreditNoteLine" : "InvoiceLine"),
            new XElement(Cbc + "ID", line.Sequence.ToString(CultureInfo.InvariantCulture)),
            new XElement(Cbc + (isCreditNote ? "CreditedQuantity" : "InvoicedQuantity"),
                new XAttribute("unitCode", line.UnitCode),
                Decimal2(line.Quantity)),
            Amount("LineExtensionAmount", LineExtension(line), invoice.Currency),
            new XElement(Cac + "Item",
                new XElement(Cbc + "Name", Truncate(line.Description, 100)),
                new XElement(Cac + "ClassifiedTaxCategory",
                    new XElement(Cbc + "ID", EffectiveCategory(line, treatment)),
                    new XElement(Cbc + "Percent", Percent(line.VatRatePercent)),
                    new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")))),
            new XElement(Cac + "Price",
                Amount("PriceAmount", line.UnitPrice, invoice.Currency)));

    /// <summary>Frozen snapshot wins; drafts derive live from the invoice's treatment snapshot.</summary>
    private static string EffectiveCategory(InvoiceLine line, VatTreatment treatment) =>
        line.VatCategoryCode ?? VatTreatmentCatalog.ResolveVatCategory(treatment, line.VatRatePercent).Code;

    private static decimal LineExtension(InvoiceLine line) => Round2(line.Quantity * line.UnitPrice);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static XElement Amount(string name, decimal value, string currency) =>
        new(Cbc + name, new XAttribute("currencyID", currency), Decimal2(value));

    private static string Decimal2(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
