using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>
/// One coherent description per VAT treatment: Dutch label, whether a VAT number is
/// required for invoicing, the standard selectable rates, the effective default rate and
/// the legal text printed on invoices. Single source for backend validation, invoice
/// snapshots and the frontend treatment/rate control (no duplicated tax logic in React).
/// </summary>
public static class VatTreatmentCatalog
{
    public sealed record VatTreatmentInfo(
        VatTreatment Treatment,
        string Label,
        bool RequiresVatNumber,
        IReadOnlyList<decimal> StandardRates,
        decimal? DefaultRatePercent,
        string? InvoiceLegalText,
        bool AllowsCustomRate);

    public static readonly IReadOnlyList<VatTreatmentInfo> All =
    [
        new(VatTreatment.DomesticVat, "Btw-plichtige klant (binnenland)",
            RequiresVatNumber: false, StandardRates: [0m, 6m, 12m, 21m], DefaultRatePercent: null,
            InvoiceLegalText: null, AllowsCustomRate: false),
        new(VatTreatment.ReverseCharge, "Medecontractant / btw verlegd",
            RequiresVatNumber: true, StandardRates: [0m], DefaultRatePercent: 0m,
            InvoiceLegalText: "Btw verlegd — art. 20 KB nr. 1 / art. 51 §4 W.Btw.", AllowsCustomRate: false),
        new(VatTreatment.IntraCommunitySupply, "Intracommunautaire klant",
            RequiresVatNumber: true, StandardRates: [0m], DefaultRatePercent: 0m,
            InvoiceLegalText: "Vrijgesteld van btw — intracommunautaire levering, art. 39bis W.Btw.", AllowsCustomRate: false),
        new(VatTreatment.ExportOutsideEu, "Exportklant (buiten EU)",
            RequiresVatNumber: false, StandardRates: [0m], DefaultRatePercent: 0m,
            InvoiceLegalText: "Vrijgesteld van btw — uitvoer buiten de EU, art. 39 W.Btw.", AllowsCustomRate: false),
        new(VatTreatment.VatExempt, "Niet-btw-plichtige / vrijgestelde klant",
            RequiresVatNumber: false, StandardRates: [0m], DefaultRatePercent: 0m,
            InvoiceLegalText: "Vrijgesteld van btw.", AllowsCustomRate: false),
        new(VatTreatment.Other, "Andere regeling",
            RequiresVatNumber: false, StandardRates: [], DefaultRatePercent: null,
            InvoiceLegalText: null, AllowsCustomRate: true),
    ];

    public static VatTreatmentInfo Resolve(VatTreatment treatment) =>
        All.FirstOrDefault(i => i.Treatment == treatment) ?? All[0];
}
