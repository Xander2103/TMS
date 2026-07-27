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
                    CustomerName = c.Name,
                    x.Line.Sequence,
                    x.Line.Description,
                    x.Line.Quantity,
                    x.Line.UnitPrice,
                    x.Line.VatRatePercent,
                    x.Line.SalesCategoryNameSnapshot,
                    x.Line.LedgerAccountNumberSnapshot,
                    x.Line.LedgerAccountNameSnapshot,
                })
            .OrderBy(x => x.InvoiceDate).ThenBy(x => x.InvoiceNumber).ThenBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        // Hard gate (§7.4): the export is only correct when EVERY line carries its frozen
        // account. Offenders are named so the user can fix the mapping and re-send/correct.
        var offenders = rows
            .Where(r => string.IsNullOrEmpty(r.LedgerAccountNumberSnapshot))
            .Select(r => r.InvoiceNumber)
            .Distinct()
            .ToList();
        if (offenders.Count > 0)
        {
            throw new DomainValidationException(
                "Boekhoudexport geblokkeerd: geen grootboekrekening vastgelegd voor factu(u)r(en) "
                + string.Join(", ", offenders.Take(10))
                + ". Configureer de mapping bij Bedrijfsinstellingen → Boekhouding en corrigeer deze facturen.");
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Boekhoudexport");
        string[] headers =
        [
            "Factuurnummer", "Factuurdatum", "Klant", "Omschrijving", "Verkoopcategorie",
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
            var net = Math.Round(row.Quantity * row.UnitPrice, 2);
            var vat = Math.Round(net * row.VatRatePercent / 100m, 2);
            sheet.Cell(rowIndex, 1).Value = row.InvoiceNumber;
            sheet.Cell(rowIndex, 2).Value = row.InvoiceDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(rowIndex, 2).Style.DateFormat.Format = "dd-mm-yyyy";
            sheet.Cell(rowIndex, 3).Value = row.CustomerName;
            sheet.Cell(rowIndex, 4).Value = row.Description;
            sheet.Cell(rowIndex, 5).Value = row.SalesCategoryNameSnapshot ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = row.LedgerAccountNumberSnapshot;
            sheet.Cell(rowIndex, 7).Value = row.LedgerAccountNameSnapshot ?? string.Empty;
            sheet.Cell(rowIndex, 8).Value = net;
            sheet.Cell(rowIndex, 9).Value = row.VatRatePercent;
            sheet.Cell(rowIndex, 10).Value = vat;
            sheet.Cell(rowIndex, 11).Value = net + vat;
            sheet.Cell(rowIndex, 12).Value = row.Currency;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
