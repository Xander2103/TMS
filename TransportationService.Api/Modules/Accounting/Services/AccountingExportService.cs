using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
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

    public async Task<byte[]> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new DomainValidationException("to", "De einddatum ligt vóór de begindatum.");
        }

        var tenantId = _tenant.TenantId;
        var rows = await _db.InvoiceLines.AsNoTracking()
            .Join(_db.Invoices.AsNoTracking().Where(i =>
                    i.TenantId == tenantId
                    && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid)
                    && i.InvoiceDate >= from && i.InvoiceDate <= to),
                l => l.InvoiceId, i => i.Id,
                (l, i) => new { Line = l, Invoice = i })
            .Where(x => x.Line.TenantId == tenantId)
            .Join(_db.Customers.AsNoTracking().Where(c => c.TenantId == tenantId),
                x => x.Invoice.CustomerId, c => c.Id,
                (x, c) => new
                {
                    x.Invoice.InvoiceNumber,
                    x.Invoice.InvoiceDate,
                    x.Invoice.Currency,
                    x.Invoice.Kind,
                    CustomerName = c.Name,
                    x.Line.Sequence,
                    x.Line.Description,
                    x.Line.Quantity,
                    x.Line.UnitPrice,
                    x.Line.VatRatePercent,
                    x.Line.SalesCategoryId,
                    x.Line.SalesCategoryNameSnapshot,
                    x.Line.LedgerAccountNumberSnapshot,
                    x.Line.LedgerAccountNameSnapshot,
                })
            .OrderBy(x => x.InvoiceDate).ThenBy(x => x.InvoiceNumber).ThenBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        // Hard gate (§7.4): the export is only correct when EVERY line carries its frozen
        // account. The message tells the user the actual fix per case: categorised lines can be
        // completed via 'Boekhoudsnapshot aanvullen' after mapping; category-less lines predate
        // the accounting module (or lost their category) and need a later export window.
        var incomplete = rows.Where(r => string.IsNullOrEmpty(r.LedgerAccountNumberSnapshot)).ToList();
        if (incomplete.Count > 0)
        {
            var fixable = incomplete.Where(r => r.SalesCategoryId is not null)
                .Select(r => r.InvoiceNumber).Distinct().Take(10).ToList();
            var legacy = incomplete.Where(r => r.SalesCategoryId is null)
                .Select(r => r.InvoiceNumber).Distinct().Take(10).ToList();
            var parts = new List<string> { "Boekhoudexport geblokkeerd:" };
            if (fixable.Count > 0)
            {
                parts.Add($"factu(u)r(en) {string.Join(", ", fixable)} missen een vastgelegde grootboekrekening — "
                    + "koppel de rekening bij Bedrijfsinstellingen → Boekhouding en gebruik 'Boekhoudsnapshot aanvullen' op de factuur.");
            }

            if (legacy.Count > 0)
            {
                parts.Add($"factu(u)r(en) {string.Join(", ", legacy)} hebben lijnen zonder verkoopcategorie "
                    + "(verzonden vóór de boekhoudmodule) — kies een exportperiode die deze facturen niet omvat.");
            }

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
        foreach (var row in rows)
        {
            // Credit notes store positive line amounts; the export carries their commercial
            // sign so revenue sums stay correct in the accounting package.
            var sign = row.Kind == InvoiceKind.CreditNote ? -1m : 1m;
            var net = sign * Math.Round(row.Quantity * row.UnitPrice, 2);
            var vat = sign * Math.Round(Math.Abs(net) * row.VatRatePercent / 100m, 2);
            sheet.Cell(rowIndex, 1).Value = row.InvoiceNumber;
            sheet.Cell(rowIndex, 2).Value = row.Kind == InvoiceKind.CreditNote ? "Creditnota" : "Factuur";
            sheet.Cell(rowIndex, 3).Value = row.InvoiceDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(rowIndex, 3).Style.DateFormat.Format = "dd-mm-yyyy";
            sheet.Cell(rowIndex, 4).Value = row.CustomerName;
            sheet.Cell(rowIndex, 5).Value = row.Description;
            sheet.Cell(rowIndex, 6).Value = row.SalesCategoryNameSnapshot ?? string.Empty;
            sheet.Cell(rowIndex, 7).Value = row.LedgerAccountNumberSnapshot;
            sheet.Cell(rowIndex, 8).Value = row.LedgerAccountNameSnapshot ?? string.Empty;
            sheet.Cell(rowIndex, 9).Value = net;
            sheet.Cell(rowIndex, 10).Value = row.VatRatePercent;
            sheet.Cell(rowIndex, 11).Value = vat;
            sheet.Cell(rowIndex, 12).Value = net + vat;
            sheet.Cell(rowIndex, 13).Value = row.Currency;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
