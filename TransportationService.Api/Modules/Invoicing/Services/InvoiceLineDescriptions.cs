using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Accounting.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// The ONE rule for what the customer reads on an invoice line, shared by the draft preview
/// (detail DTO + draft PDF) and by Send (which freezes the result on the line). Keeping the
/// draft and the finalized document on the same function is what guarantees "what you preview
/// is what gets sent".
/// </summary>
public static class InvoiceLineDescriptions
{
    /// <summary>
    /// True when the stored text is still the sales code's own default wording (empty, the
    /// internal name, or the Dutch invoice description). Anything else was typed or configured
    /// for this specific line/order and is the user's wording — it is never translated away.
    /// </summary>
    public static bool IsDefaultText(string? currentDescription, SalesCategory salesCode) =>
        string.IsNullOrWhiteSpace(currentDescription)
        || string.Equals(currentDescription, salesCode.Name, StringComparison.Ordinal)
        || string.Equals(currentDescription, salesCode.InvoiceDescriptionNl, StringComparison.Ordinal);

    /// <summary>
    /// The customer-facing description: the approved wording for the invoice language when the
    /// line still carries the code's default text, otherwise the line's own text unchanged.
    /// Without a sales code the stored text is all there is.
    /// </summary>
    public static string CustomerFacing(string? currentDescription, SalesCategory? salesCode, string? invoiceLanguageCode)
    {
        if (salesCode is null || !IsDefaultText(currentDescription, salesCode))
        {
            return currentDescription ?? string.Empty;
        }

        var approved = InvoiceLineFiscalResolver.DescriptionFor(salesCode, invoiceLanguageCode);
        return string.IsNullOrWhiteSpace(approved) ? currentDescription ?? string.Empty : approved;
    }
}
