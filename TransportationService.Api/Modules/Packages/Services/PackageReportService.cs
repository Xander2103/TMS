using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

public interface IPackageReportService
{
    IReadOnlyList<string> ReportKeys { get; }

    Task<(byte[] Content, string FileName)?> BuildAsync(
        string report, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// Package XLSX reports (ClosedXML). Every cell is written as a typed value — text stays
/// text, so spreadsheet formula injection is structurally impossible. Every workbook gets
/// a criteria sheet (period, tenant, user, generated-at).
/// </summary>
public class PackageReportService : IPackageReportService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly TimeProvider _timeProvider;

    public PackageReportService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<string> ReportKeys { get; } =
    [
        "order-packages", "trip-packages", "scan-activity", "package-exceptions",
        "missing-packages", "damaged-packages", "returns", "delivery-performance",
    ];

    public async Task<(byte[] Content, string FileName)?> BuildAsync(
        string report, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var built = report switch
        {
            "order-packages" => await BuildOrderPackagesAsync(workbook, from, to, cancellationToken),
            "trip-packages" => await BuildTripPackagesAsync(workbook, from, to, cancellationToken),
            "scan-activity" => await BuildScanActivityAsync(workbook, from, to, cancellationToken),
            "package-exceptions" => await BuildExceptionsAsync(workbook, from, to, cancellationToken),
            "missing-packages" => await BuildByStatusAsync(workbook, "Vermiste colli", [PackageLifecycleStatus.Missing], cancellationToken),
            "damaged-packages" => await BuildByStatusAsync(workbook, "Beschadigde colli", [PackageLifecycleStatus.Damaged, PackageLifecycleStatus.Quarantined], cancellationToken),
            "returns" => await BuildByStatusAsync(workbook, "Retouren",
                [PackageLifecycleStatus.ReturnPending, PackageLifecycleStatus.ReturnLoaded,
                 PackageLifecycleStatus.ReturnedToDepot, PackageLifecycleStatus.ReturnedToSender,
                 PackageLifecycleStatus.RedeliveryPlanned], cancellationToken),
            "delivery-performance" => await BuildDeliveryPerformanceAsync(workbook, from, to, cancellationToken),
            _ => false,
        };
        if (!built)
        {
            return null;
        }

        AddCriteriaSheet(workbook, report, from, to);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm");
        return (stream.ToArray(), $"colli-{report}-{stamp}.xlsx");
    }

    private static void Header(IXLWorksheet sheet, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = titles[i];
        }
        sheet.Row(1).Style.Font.SetBold();
        sheet.SheetView.FreezeRows(1);
    }

    private sealed record PackageRow(Package Package, string OrderNumber, string CustomerName);

    private async Task<List<PackageRow>> LoadPackageRowsAsync(
        IReadOnlyList<PackageLifecycleStatus>? statuses, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = _dbContext.Packages.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId);
        if (statuses is { Count: > 0 })
        {
            query = query.Where(p => statuses.Contains(p.CurrentLifecycleStatus));
        }
        if (from is { } f)
        {
            var fromUtc = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(p => p.CreatedAt >= fromUtc);
        }
        if (to is { } tt)
        {
            var toUtc = tt.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(p => p.CreatedAt < toUtc);
        }

        var raw = await (from p in query
                         join o in _dbContext.TransportOrders.AsNoTracking()
                                 .Where(o => o.TenantId == _tenantContext.TenantId)
                             on p.TransportOrderId equals o.Id
                         join c in _dbContext.Customers.AsNoTracking()
                                 .Where(c => c.TenantId == _tenantContext.TenantId)
                             on o.CustomerId equals c.Id
                         orderby o.OrderNumber, p.PackageNumber
                         select new { p, o.OrderNumber, CustomerName = c.Name })
            .ToListAsync(cancellationToken);
        return raw.Select(r => new PackageRow(r.p, r.OrderNumber, r.CustomerName)).ToList();
    }

    private static void WritePackageSheet(IXLWorksheet sheet, List<PackageRow> rows)
    {
        Header(sheet, "Collinummer", "Omschrijving", "Aantal", "Eenheid", "Status", "Melding",
            "Opdracht", "Klant", "Verplicht", "Groep", "Aangemaakt");
        var line = 2;
        foreach (var row in rows)
        {
            sheet.Cell(line, 1).Value = row.Package.PackageNumber;
            sheet.Cell(line, 2).Value = row.Package.Description;
            sheet.Cell(line, 3).Value = row.Package.Quantity;
            sheet.Cell(line, 4).Value = row.Package.UnitTypeLabel ?? row.Package.UnitType.ToString();
            sheet.Cell(line, 5).Value = row.Package.CurrentLifecycleStatus.ToString();
            sheet.Cell(line, 6).Value = row.Package.CurrentExceptionStatus.ToString();
            sheet.Cell(line, 7).Value = row.OrderNumber;
            sheet.Cell(line, 8).Value = row.CustomerName;
            sheet.Cell(line, 9).Value = row.Package.IsMandatory ? "Ja" : "Nee";
            sheet.Cell(line, 10).Value = row.Package.ParentPackageId is null ? "" : "In groep";
            sheet.Cell(line, 11).Value = row.Package.CreatedAt.ToString("dd-MM-yyyy HH:mm");
            line++;
        }
        sheet.Columns().AdjustToContents();
    }

    private async Task<bool> BuildOrderPackagesAsync(XLWorkbook workbook, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await LoadPackageRowsAsync(null, from, to, cancellationToken);
        WritePackageSheet(workbook.Worksheets.Add("Colli per opdracht"), rows);
        return true;
    }

    private async Task<bool> BuildTripPackagesAsync(XLWorkbook workbook, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.TripDate >= from && t.TripDate <= to)
            .Join(_dbContext.TripOrders.AsNoTracking().Where(x => x.TenantId == _tenantContext.TenantId),
                t => t.Id, to2 => to2.TripId, (t, to2) => new { t, to2.TransportOrderId })
            .Join(_dbContext.Packages.AsNoTracking().Where(p => p.TenantId == _tenantContext.TenantId),
                x => x.TransportOrderId, p => p.TransportOrderId, (x, p) => new { x.t, p })
            .OrderBy(x => x.t.TripNumber).ThenBy(x => x.p.PackageNumber)
            .ToListAsync(cancellationToken);
        _ = fromUtc; _ = toUtc;

        var sheet = workbook.Worksheets.Add("Colli per rit");
        Header(sheet, "Rit", "Ritdatum", "Ritstatus", "Collinummer", "Omschrijving", "Collistatus", "Melding");
        var line = 2;
        foreach (var row in rows)
        {
            sheet.Cell(line, 1).Value = row.t.TripNumber;
            sheet.Cell(line, 2).Value = row.t.TripDate.ToString("dd-MM-yyyy");
            sheet.Cell(line, 3).Value = row.t.Status.ToString();
            sheet.Cell(line, 4).Value = row.p.PackageNumber;
            sheet.Cell(line, 5).Value = row.p.Description;
            sheet.Cell(line, 6).Value = row.p.CurrentLifecycleStatus.ToString();
            sheet.Cell(line, 7).Value = row.p.CurrentExceptionStatus.ToString();
            line++;
        }
        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildScanActivityAsync(XLWorkbook workbook, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await _dbContext.ScanEvents.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc)
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == _tenantContext.TenantId),
                e => e.TripId, t => t.Id, (e, t) => new { e, t.TripNumber })
            .OrderBy(x => x.e.OccurredAt)
            .ToListAsync(cancellationToken);

        var packageIds = rows.Where(x => x.e.PackageId is not null).Select(x => x.e.PackageId!.Value).Distinct().ToList();
        var packageNumbers = packageIds.Count == 0
            ? []
            : await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == _tenantContext.TenantId && packageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.PackageNumber })
                .ToDictionaryAsync(p => p.Id, p => p.PackageNumber, cancellationToken);

        var sheet = workbook.Worksheets.Add("Scanactiviteit");
        Header(sheet, "Tijdstip", "Rit", "Type", "Resultaat", "Colli-uitkomst", "Collinummer", "Barcode", "Aantal", "Schade");
        var line = 2;
        foreach (var row in rows)
        {
            sheet.Cell(line, 1).Value = row.e.OccurredAt.ToString("dd-MM-yyyy HH:mm:ss");
            sheet.Cell(line, 2).Value = row.TripNumber;
            sheet.Cell(line, 3).Value = row.e.ScanType.ToString();
            sheet.Cell(line, 4).Value = row.e.Result.ToString();
            sheet.Cell(line, 5).Value = row.e.PackageOutcome ?? "";
            sheet.Cell(line, 6).Value = row.e.PackageId is { } pid ? packageNumbers.GetValueOrDefault(pid, "") : "";
            sheet.Cell(line, 7).Value = row.e.Barcode ?? "";
            sheet.Cell(line, 8).Value = row.e.Quantity;
            sheet.Cell(line, 9).Value = row.e.Damaged ? "Ja" : "";
            line++;
        }
        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildExceptionsAsync(XLWorkbook workbook, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.PackageId != null
                        && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        var packageIds = rows.Select(e => e.PackageId!.Value).Distinct().ToList();
        var packageNumbers = packageIds.Count == 0
            ? []
            : await _dbContext.Packages.AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.TenantId == _tenantContext.TenantId && packageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.PackageNumber })
                .ToDictionaryAsync(p => p.Id, p => p.PackageNumber, cancellationToken);

        var sheet = workbook.Worksheets.Add("Colli-meldingen");
        Header(sheet, "Datum", "Type", "Ernst", "Status", "Collinummer", "Omschrijving", "Opgelost op", "Oplossing");
        var line = 2;
        foreach (var row in rows)
        {
            sheet.Cell(line, 1).Value = row.OccurredAt.ToString("dd-MM-yyyy HH:mm");
            sheet.Cell(line, 2).Value = row.Type.ToString();
            sheet.Cell(line, 3).Value = row.Severity.ToString();
            sheet.Cell(line, 4).Value = row.Status.ToString();
            sheet.Cell(line, 5).Value = packageNumbers.GetValueOrDefault(row.PackageId!.Value, "");
            sheet.Cell(line, 6).Value = row.Description;
            sheet.Cell(line, 7).Value = row.ResolvedAt?.ToString("dd-MM-yyyy HH:mm") ?? "";
            sheet.Cell(line, 8).Value = row.ResolutionNote ?? "";
            line++;
        }
        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildByStatusAsync(
        XLWorkbook workbook, string title, IReadOnlyList<PackageLifecycleStatus> statuses, CancellationToken cancellationToken)
    {
        var rows = await LoadPackageRowsAsync(statuses, null, null, cancellationToken);
        WritePackageSheet(workbook.Worksheets.Add(title), rows);
        return true;
    }

    private async Task<bool> BuildDeliveryPerformanceAsync(XLWorkbook workbook, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Outcomes come from the custody chain, not current status: a later return must
        // not erase that the delivery attempt happened in the period.
        var events = await _dbContext.PackageEvents.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId
                        && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc
                        && (e.EventType == PackageEventType.Delivered
                            || e.EventType == PackageEventType.Refused
                            || e.EventType == PackageEventType.PartialDelivery
                            || e.EventType == PackageEventType.DamageReported
                            || e.EventType == PackageEventType.MarkedMissing))
            .ToListAsync(cancellationToken);

        var sheet = workbook.Worksheets.Add("Leverprestatie");
        Header(sheet, "Datum", "Geleverd", "Geweigerd", "Gedeeltelijk", "Schade gemeld", "Vermist gemeld", "Succes %");
        var line = 2;
        foreach (var day in events.GroupBy(e => DateOnly.FromDateTime(e.OccurredAt)).OrderBy(g => g.Key))
        {
            var delivered = day.Count(e => e.EventType == PackageEventType.Delivered);
            var refused = day.Count(e => e.EventType == PackageEventType.Refused);
            var partial = day.Count(e => e.EventType == PackageEventType.PartialDelivery);
            var damaged = day.Count(e => e.EventType == PackageEventType.DamageReported);
            var missing = day.Count(e => e.EventType == PackageEventType.MarkedMissing);
            var attempts = delivered + refused + partial;
            sheet.Cell(line, 1).Value = day.Key.ToString("dd-MM-yyyy");
            sheet.Cell(line, 2).Value = delivered;
            sheet.Cell(line, 3).Value = refused;
            sheet.Cell(line, 4).Value = partial;
            sheet.Cell(line, 5).Value = damaged;
            sheet.Cell(line, 6).Value = missing;
            sheet.Cell(line, 7).Value = attempts == 0 ? 0 : Math.Round(100m * delivered / attempts, 1);
            line++;
        }
        sheet.Columns().AdjustToContents();
        return true;
    }

    private void AddCriteriaSheet(XLWorkbook workbook, string report, DateOnly from, DateOnly to)
    {
        var sheet = workbook.Worksheets.Add("Criteria");
        sheet.Cell(1, 1).Value = "Rapport";
        sheet.Cell(1, 2).Value = report;
        sheet.Cell(2, 1).Value = "Periode van";
        sheet.Cell(2, 2).Value = from.ToString("dd-MM-yyyy");
        sheet.Cell(3, 1).Value = "Periode tot";
        sheet.Cell(3, 2).Value = to.ToString("dd-MM-yyyy");
        sheet.Cell(4, 1).Value = "Gegenereerd op";
        sheet.Cell(4, 2).Value = _timeProvider.GetUtcNow().UtcDateTime.ToString("dd-MM-yyyy HH:mm") + " UTC";
        sheet.Cell(5, 1).Value = "Door gebruiker";
        sheet.Cell(5, 2).Value = _currentUserContext.CurrentUserId?.ToString() ?? "";
        sheet.Column(1).Style.Font.SetBold();
        sheet.Columns().AdjustToContents();
    }
}
