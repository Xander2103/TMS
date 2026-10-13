using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// H-06 — "is this credit-note line a COPY of a credited line, or one typed on the credit note
/// itself?" A copy mirrors the document it credits: it is never re-frozen, never re-derived from
/// live master data, never recategorised, and it displays its copied freeze even while its own
/// document is still Draft. A typed line has nothing to mirror and behaves like any other line.
///
/// There is no persisted "copied" flag and adding one would be a schema change, so the marker is
/// the fiscal data a copy always carries and a freshly typed draft line never does. The disjunction
/// deliberately spans EVERY freeze field rather than one of them: <see
/// cref="InvoiceLine.VatTreatmentSnapshot"/> and the rest of the sprint-5H block only exist since
/// 2026-08-28 and were never backfilled, so a line frozen before that carries a ledger snapshot and
/// a UBL category with a null treatment snapshot. Keying on any single field would miss exactly
/// those rows and re-derive their credit notes from today's master data.
///
/// <see cref="Entities.InvoiceLine.VatCategoryCode"/> is the oldest and broadest of the signals, and
/// <c>InvoiceService.CreateCreditNoteAsync</c> guarantees it on every copy (deriving the credited
/// header's own category for a line that predates even that field, which is precisely what Send
/// would have stamped). Every credit note created from this version on therefore carries a total
/// marker; only a credit note left in Draft by an older build can still be blind.
/// </summary>
public static class InvoiceLineMirror
{
    /// <summary>True when the line carries any trace of a Send-time fiscal freeze.</summary>
    public static bool HasFrozenFiscalData(InvoiceLine line) =>
        line.VatCategoryCode is not null
        || line.VatTreatmentSnapshot is not null
        || line.VatTreatmentSourceSnapshot is not null
        || line.VatLegalTextSnapshot is not null
        || line.SalesCodeSnapshot is not null
        || line.SalesCategoryNameSnapshot is not null
        || line.DescriptionLanguageSnapshot is not null
        || line.CostCentreSnapshot is not null
        || line.LedgerAccountId is not null
        || line.LedgerAccountNumberSnapshot is not null
        || line.LedgerAccountNameSnapshot is not null;

    /// <summary>True for a credit-note line copied from the document it credits.</summary>
    public static bool IsMirrored(Invoice invoice, InvoiceLine line) =>
        invoice.Kind == InvoiceKind.CreditNote && HasFrozenFiscalData(line);

    /// <summary>
    /// The mirrored lines of this document, captured as ids. The freeze passes at Send STAMP
    /// snapshot fields on the lines they process, so a marker evaluated between two passes would
    /// see a line the first pass just froze and mistake it for a copy. Every caller that freezes
    /// therefore resolves the set once, before the first pass touches anything.
    /// </summary>
    public static IReadOnlySet<Guid> MirroredIds(Invoice invoice) =>
        invoice.Lines.Where(l => !l.IsDeleted && IsMirrored(invoice, l)).Select(l => l.Id).ToHashSet();
}
