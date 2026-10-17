using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Money;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Accounting.Services;

public interface IAccountingExportService
{
    /// <summary>
    /// XLSX accounting export of all Sent/Paid invoice lines in the invoice-date window,
    /// reading EXCLUSIVELY the ledger snapshots frozen at Send. Throws a validation error
    /// naming the offending invoices when any included line lacks a snapshotted account.
    /// </summary>
    Task<byte[]> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// ACC-M9: the export's own validation report — every issued invoice in the window that
    /// would block the export, with the reason per line. Nothing is ever silently omitted:
    /// the export refuses until this list is empty.
    /// </summary>
    Task<AccountingExportPreflightDto> PreflightAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public class AccountingExportService : IAccountingExportService
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;

    public AccountingExportService(TransportationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private sealed record ExportInvoice(Invoice Invoice, string CustomerName, IReadOnlyList<InvoiceLine> Lines);

    private async Task<IReadOnlyList<ExportInvoice>> LoadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new DomainValidationException("to", "De einddatum ligt vóór de begindatum.");
        }

        var tenantId = _tenant.TenantId;
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId
                        && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid)
                        && i.InvoiceDate >= from && i.InvoiceDate <= to)
            .Join(_db.Customers.AsNoTracking().Where(c => c.TenantId == tenantId),
                // ACC-H4: the booked customer is the one frozen on the document, never today's master data.
                i => i.CustomerId, c => c.Id, (i, c) => new { Invoice = i, CustomerName = i.CustomerNameSnapshot ?? c.Name })
            .OrderBy(x => x.Invoice.InvoiceDate).ThenBy(x => x.Invoice.InvoiceNumber)
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(x => x.Invoice.Id).ToList();
        var lines = await _db.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && invoiceIds.Contains(l.InvoiceId))
            .OrderBy(l => l.Sequence)
            .ToListAsync(cancellationToken);
        var linesByInvoice = lines.ToLookup(l => l.InvoiceId);
        return invoices
            .Select(x => new ExportInvoice(x.Invoice, x.CustomerName, linesByInvoice[x.Invoice.Id].ToList()))
            .ToList();
    }

    /// <summary>
    /// Hard gate (§7.4): the export is only correct when EVERY line carries its frozen account.
    /// The reason per line tells the user the actual fix: categorised lines can be completed
    /// via 'Grootboekgegevens aanvullen' after mapping; category-less lines predate the
    /// accounting module (or lost their category) and need a later export window.
    /// </summary>
    private static IReadOnlyList<string> BlockingReasons(InvoiceLine line)
    {
        if (!string.IsNullOrEmpty(line.LedgerAccountNumberSnapshot))
        {
            return [];
        }

        return line.SalesCategoryId is null
            ? [$"Lijn {line.Sequence} '{line.Description}': geen verkoopcategorie, dus geen grootboekrekening af te leiden (verzonden vóór de boekhoudmodule) — kies een exportperiode die deze factuur niet omvat."]
            : [$"Lijn {line.Sequence} '{line.Description}': geen vastgelegde grootboekrekening — koppel de rekening bij Bedrijfsinstellingen → Boekhouding en gebruik 'Grootboekgegevens aanvullen' op de factuur."];
    }

    public async Task<AccountingExportPreflightDto> PreflightAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var invoices = await LoadAsync(from, to, cancellationToken);
        var blocking = invoices
            .Select(x => new AccountingExportPreflightInvoiceDto(
                x.Invoice.Id, x.Invoice.InvoiceNumber, x.Lines.SelectMany(BlockingReasons).ToList()))
            .Where(x => x.Reasons.Count > 0)
            .ToList();
        return new AccountingExportPreflightDto(blocking.Count == 0, blocking);
    }

    public async Task<byte[]> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var invoices = await LoadAsync(from, to, cancellationToken);

        var incomplete = invoices.Where(x => x.Lines.Any(l => string.IsNullOrEmpty(l.LedgerAccountNumberSnapshot))).ToList();
        if (incomplete.Count > 0)
        {
            var fixable = incomplete.Where(x => x.Lines.Any(l => string.IsNullOrEmpty(l.LedgerAccountNumberSnapshot) && l.SalesCategoryId is not null))
                .Select(x => x.Invoice.InvoiceNumber).Distinct().Take(10).ToList();
            var legacy = incomplete.Where(x => x.Lines.Any(l => string.IsNullOrEmpty(l.LedgerAccountNumberSnapshot) && l.SalesCategoryId is null))
                .Select(x => x.Invoice.InvoiceNumber).Distinct().Take(10).ToList();
            var parts = new List<string> { $"Boekhoudexport geblokkeerd: {incomplete.Count} factu(u)r(en) in de periode missen een vastgelegde grootboekrekening." };
            if (fixable.Count > 0)
            {
                parts.Add($"Te corrigeren via 'Grootboekgegevens aanvullen' (Boekhoudsnapshot aanvullen) op de factuur, na het koppelen van de rekening bij Bedrijfsinstellingen → Boekhouding: {string.Join(", ", fixable)}.");
            }

            if (legacy.Count > 0)
            {
                parts.Add($"Lijnen zonder verkoopcategorie (verzonden vóór de boekhoudmodule), kies een exportperiode die deze facturen niet omvat: {string.Join(", ", legacy)}.");
            }

            parts.Add("De exportcontrole (export/preflight) toont alle betrokken facturen en lijnen; er wordt nooit een factuur stilzwijgend overgeslagen.");
            throw new DomainValidationException(string.Join(" ", parts));
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Boekhoudexport");
        string[] headers =
        [
            "Factuurnummer", "Type", "Factuurdatum", "Klant", "Omschrijving", "Verkoopcategorie",
            "Grootboekrekening", "Rekeningnaam", "Netto", "Btw %", "Btw-bedrag", "Bruto", "Valuta",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var entry in invoices)
        {
            // Credit notes store positive line amounts; the export carries their commercial
            // sign so revenue sums stay correct in the accounting package.
            var sign = entry.Invoice.Kind == InvoiceKind.CreditNote ? -1m : 1m;
            foreach (var (line, net, vat) in ExportAmounts(entry.Invoice, entry.Lines))
            {
                sheet.Cell(rowIndex, 1).Value = entry.Invoice.InvoiceNumber;
                sheet.Cell(rowIndex, 2).Value = entry.Invoice.Kind == InvoiceKind.CreditNote ? "Creditnota" : "Factuur";
                sheet.Cell(rowIndex, 3).Value = entry.Invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(rowIndex, 3).Style.DateFormat.Format = "dd-mm-yyyy";
                sheet.Cell(rowIndex, 4).Value = entry.CustomerName;
                sheet.Cell(rowIndex, 5).Value = line.Description;
                sheet.Cell(rowIndex, 6).Value = line.SalesCategoryNameSnapshot ?? string.Empty;
                sheet.Cell(rowIndex, 7).Value = line.LedgerAccountNumberSnapshot;
                sheet.Cell(rowIndex, 8).Value = line.LedgerAccountNameSnapshot ?? string.Empty;
                sheet.Cell(rowIndex, 9).Value = sign * net;
                sheet.Cell(rowIndex, 10).Value = line.VatRatePercent;
                sheet.Cell(rowIndex, 11).Value = sign * vat;
                sheet.Cell(rowIndex, 12).Value = sign * (net + vat);
                sheet.Cell(rowIndex, 13).Value = entry.Invoice.Currency;
                rowIndex++;
            }
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// ACC-M9: the export never recomputes money its own way. Line net = <see cref="MoneyRounding.LineTotal"/>
    /// (the invoice line amount); VAT is the invoice's own per-(category, rate)-group amount
    /// (<see cref="InvoiceTotals"/>, half away from zero), spread over the lines of the group:
    /// each line gets its rounded share and the group's rounding difference (at most a few cents)
    /// lands on the group's last line, so the exported rows add up to exactly the invoice's VAT
    /// and total — the same figures as the PDF, the UBL and the detail screen.
    /// </summary>
    internal static IReadOnlyList<(InvoiceLine Line, decimal Net, decimal Vat)> ExportAmounts(Invoice invoice, IReadOnlyList<InvoiceLine> lines)
    {
        var treatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed)
            ? parsed
            : VatTreatment.DomesticVat;
        var result = new Dictionary<Guid, (decimal Net, decimal Vat)>();
        foreach (var group in lines
                     .Where(l => !l.IsDeleted)
                     .GroupBy(l => (
                         Category: l.VatCategoryCode ?? VatTreatmentCatalog.ResolveVatCategory(treatment, l.VatRatePercent).Code,
                         l.VatRatePercent)))
        {
            var groupLines = group.ToList();
            var nets = groupLines.Select(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice)).ToList();
            var groupVat = MoneyRounding.Round2(Math.Round(nets.Sum(), 2) * group.Key.VatRatePercent / 100m);
            var lineVats = nets.Select(net => MoneyRounding.Round2(net * group.Key.VatRatePercent / 100m)).ToList();
            lineVats[^1] += groupVat - lineVats.Sum();
            for (var i = 0; i < groupLines.Count; i++)
            {
                result[groupLines[i].Id] = (nets[i], lineVats[i]);
            }
        }

        return lines.Where(l => !l.IsDeleted).Select(l => (l, result[l.Id].Net, result[l.Id].Vat)).ToList();
    }
}
