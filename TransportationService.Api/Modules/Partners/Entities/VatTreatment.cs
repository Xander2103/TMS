namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>
/// How VAT is applied on invoices for a customer. Deliberately separate from the VAT
/// percentage: treatment describes the regime, the rate describes the number.
/// </summary>
public enum VatTreatment
{
    /// <summary>Binnenlandse BTW — normal domestic VAT.</summary>
    DomesticVat,

    /// <summary>BTW verlegd — reverse charge (art. 12/51, domestic B2B constructions).</summary>
    ReverseCharge,

    /// <summary>Intracommunautaire levering — 0% with valid EU VAT number.</summary>
    IntraCommunitySupply,

    /// <summary>Uitvoer buiten de EU — export, 0%.</summary>
    ExportOutsideEu,

    /// <summary>Vrijgesteld van BTW.</summary>
    VatExempt,

    /// <summary>Afwijkende regeling — explain in VatNotes.</summary>
    Other,
}
