namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// Wave 2 §3: the complete PDF string catalog per document language. The language freezes on
/// the invoice at creation (Invoice.LanguageCode); unknown/missing codes render Dutch. Adding
/// a language = adding one record here — the renderer never hardcodes label text.
/// </summary>
public sealed record InvoicePdfStrings(
    string Title,
    string CreditNoteTitle,
    string InvoiceNumberLabel,
    string CreditNoteNumberLabel,
    string DateLabel,
    string DueDateLabel,
    string CreditsInvoiceLabel,
    string PoNumberLabel,
    string BillingAddressHeading,
    string VatNumberLabel,
    string DescriptionColumn,
    string QuantityColumn,
    string PriceColumn,
    string VatColumn,
    string TotalColumn,
    string VatBreakdownHeading,
    /// <summary>"21% over EUR 100.00" — the word between rate and base amount.</summary>
    string VatOverWord,
    string SubtotalLabel,
    string VatLabel,
    string TotalLabel,
    string PaymentHeading,
    string PaymentReferenceLabel,
    /// <summary>.NET date format string for all dates on the document.</summary>
    string DateFormat,
    /// <summary>Unit word after an hourly service quantity ("2 uur").</summary>
    string HourUnit = "uur",
    /// <summary>Unit word after a per-stop service quantity ("3 stops").</summary>
    string StopsUnit = "stops",
    /// <summary>Label of a diesel-surcharge line ("Dieseltoeslag 12%").</summary>
    string DieselSurcharge = "Dieseltoeslag",
    /// <summary>Suffix for a diesel surcharge computed over the invoice subtotal.</summary>
    string OnInvoiceSubtotal = "op factuursubtotaal",
    /// <summary>Stamp next to the title on a draft preview ("FACTUUR — CONCEPT").</summary>
    string DraftLabel = "CONCEPT",
    /// <summary>Suffix on an explicitly free line ("… (gratis)").</summary>
    string FreeLabel = "gratis",
    /// <summary>Fallback goods wording when an order has no goods description at all.</summary>
    string TransportLabel = "Transport",
    /// <summary>Unit column header of the line table.</summary>
    string UnitColumn = "Eenheid",
    /// <summary>Company / enterprise number label ("Ondernemingsnr.").</summary>
    string CompanyNumberLabel = "Ondernemingsnr.",
    /// <summary>"Betaling binnen {0} dagen, uiterlijk op {1}." — payment terms sentence.</summary>
    string PaymentTermsFormat = "Betaling binnen {0} dagen, uiterlijk op {1}.",
    /// <summary>Totals label on a credit note ("Te crediteren").</summary>
    string CreditTotalLabel = "Te crediteren",
    /// <summary>Reason line on a credit note.</summary>
    string CreditReasonLabel = "Reden")
{
    /// <summary>The payment terms sentence for this language, or null when there is no term.</summary>
    public string? PaymentTermsText(int termDays, DateOnly dueDate) =>
        termDays <= 0 ? null : string.Format(PaymentTermsFormat, termDays, dueDate.ToString(DateFormat));

    public static readonly InvoicePdfStrings Nl = new(
        "FACTUUR", "CREDITNOTA", "Factuurnummer", "Creditnotanummer", "Datum", "Vervaldatum",
        "Crediteert factuur", "PO-nummer", "Factuuradres", "BTW",
        "Omschrijving", "Aantal", "Prijs", "BTW%", "Totaal",
        "BTW-overzicht", "over", "Subtotaal", "BTW", "Totaal",
        "Betaalgegevens", "Mededeling", "dd-MM-yyyy",
        "uur", "stops", "Dieseltoeslag", "op factuursubtotaal", "CONCEPT",
        "gratis", "Transport", "Eenheid", "Ondernemingsnr.", "Betaling binnen {0} dagen, uiterlijk op {1}.", "Te crediteren", "Reden");

    public static readonly InvoicePdfStrings Fr = new(
        "FACTURE", "NOTE DE CRÉDIT", "Numéro de facture", "Numéro de note de crédit", "Date", "Échéance",
        "Crédite la facture", "Numéro de commande", "Adresse de facturation", "TVA",
        "Description", "Quantité", "Prix", "TVA%", "Total",
        "Récapitulatif TVA", "sur", "Sous-total", "TVA", "Total",
        "Informations de paiement", "Communication", "dd/MM/yyyy",
        "h", "arrêts", "Surcharge gasoil", "sur le sous-total de la facture", "BROUILLON",
        "gratuit", "Transport", "Unité", "N° d'entreprise", "Paiement endéans {0} jours, au plus tard le {1}.", "À créditer", "Motif");

    public static readonly InvoicePdfStrings En = new(
        "INVOICE", "CREDIT NOTE", "Invoice number", "Credit note number", "Date", "Due date",
        "Credits invoice", "PO number", "Billing address", "VAT",
        "Description", "Quantity", "Price", "VAT%", "Total",
        "VAT breakdown", "on", "Subtotal", "VAT", "Total",
        "Payment details", "Reference", "dd/MM/yyyy",
        "h", "stops", "Diesel surcharge", "on invoice subtotal", "DRAFT",
        "free of charge", "Transport", "Unit", "Company no.", "Payment within {0} days, at the latest on {1}.", "Credit amount", "Reason");

    public static readonly InvoicePdfStrings De = new(
        "RECHNUNG", "GUTSCHRIFT", "Rechnungsnummer", "Gutschriftnummer", "Datum", "Fälligkeitsdatum",
        "Gutschrift zur Rechnung", "Bestellnummer", "Rechnungsadresse", "USt.",
        "Beschreibung", "Menge", "Preis", "USt.%", "Gesamt",
        "USt.-Übersicht", "auf", "Zwischensumme", "USt.", "Gesamt",
        "Zahlungsinformationen", "Verwendungszweck", "dd.MM.yyyy",
        "Std.", "Stopps", "Dieselzuschlag", "auf Rechnungszwischensumme", "ENTWURF",
        "kostenlos", "Transport", "Einheit", "Unternehmensnr.", "Zahlung innerhalb von {0} Tagen, spätestens am {1}.", "Gutschriftbetrag", "Grund");

    public static InvoicePdfStrings For(string? languageCode) =>
        languageCode?.Trim().ToLowerInvariant() switch
        {
            "fr" => Fr,
            "en" => En,
            "de" => De,
            _ => Nl,
        };
}
