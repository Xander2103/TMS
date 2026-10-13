using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// Single source for invoice VAT totals: per (VAT category, rate) group rounding, identical
/// to the Peppol UBL document (<c>UblDocumentBuilder</c>). The detail DTO, the PDF and the
/// generated XML must always show the same payable amount — never compute VAT ad hoc.
/// </summary>
public static class InvoiceTotals
{
    public static decimal VatTotal(IEnumerable<InvoiceLine> lines, string? vatTreatmentSnapshot)
    {
        var treatment = Enum.TryParse<VatTreatment>(vatTreatmentSnapshot, out var parsed)
            ? parsed
            : VatTreatment.DomesticVat;
        return lines
            .Where(l => !l.IsDeleted)
            .GroupBy(l => (
                Category: l.VatCategoryCode ?? VatTreatmentCatalog.ResolveVatCategory(treatment, l.VatRatePercent).Code,
                l.VatRatePercent))
            .Sum(g =>
            {
                var taxable = Math.Round(g.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), 2);
                return Math.Round(taxable * g.Key.VatRatePercent / 100m, 2);
            });
    }
}
