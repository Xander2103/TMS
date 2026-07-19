using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Services;

namespace TransportationService.Api.Modules.Reporting.Services;

public interface IKpiExportService
{
    /// <summary>Known report keys (kebab-case), in menu order.</summary>
    IReadOnlyList<string> ReportKeys { get; }

    /// <summary>Builds the XLSX for a report key; null when the key is unknown.</summary>
    Task<(byte[] Content, string FileName)?> BuildAsync(
        string report, KpiFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// Real XLSX exports (ClosedXML). Every workbook carries a "Criteria" sheet (filters,
/// generated-at, tenant, user). All text cells are written as text values — ClosedXML never
/// interprets a string as a formula unless FormulaA1 is set, which this service never does —
/// so "=SUM(...)"-style names cannot become formulas. Dates render dd-MM-yyyy, numbers with
/// European grouping.
/// </summary>
public class KpiExportService : IKpiExportService
{
    private const string DateFormat = "dd-MM-yyyy";
    private const string MoneyFormat = "#,##0.00";
    private const string NumberFormat1 = "#,##0.0";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IKpiQueryService _kpiQueryService;
    private readonly ICostRateService _costRateService;
    private readonly TimeProvider _timeProvider;

    public KpiExportService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IKpiQueryService kpiQueryService,
        ICostRateService costRateService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _kpiQueryService = kpiQueryService;
        _costRateService = costRateService;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<string> ReportKeys { get; } =
    [
        "trip-profitability", "customer-profitability", "vehicle-utilisation", "driver-hours",
        "empty-km", "fuel", "co2", "eta-performance", "delivery-reliability",
    ];

    public async Task<(byte[] Content, string FileName)?> BuildAsync(
        string report, KpiFilter filter, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var built = report switch
        {
            "trip-profitability" => await BuildTripProfitabilityAsync(workbook, filter, cancellationToken),
            "customer-profitability" => await BuildCustomerProfitabilityAsync(workbook, filter, cancellationToken),
            "vehicle-utilisation" => await BuildVehicleUtilisationAsync(workbook, filter, cancellationToken),
            "driver-hours" => await BuildDriverHoursAsync(workbook, filter, cancellationToken),
            "empty-km" => await BuildEmptyKmAsync(workbook, filter, cancellationToken),
            "fuel" => await BuildFuelAsync(workbook, filter, cancellationToken),
            "co2" => await BuildCo2Async(workbook, filter, cancellationToken),
            "eta-performance" => await BuildEtaPerformanceAsync(workbook, filter, cancellationToken),
            "delivery-reliability" => await BuildDeliveryReliabilityAsync(workbook, filter, cancellationToken),
            _ => false,
        };
        if (!built)
        {
            return null;
        }

        await AddCriteriaSheetAsync(workbook, report, filter, cancellationToken);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm");
        return (stream.ToArray(), $"kpi-{report}-{stamp}.xlsx");
    }

    // ------------------------------------------------------------- reports

    private async Task<bool> BuildTripProfitabilityAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await _kpiQueryService.GetTripProfitabilityAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Ritrendement",
            ["Rit", "Datum", "Chauffeur", "Voertuig", "Klant(en)", "Omzet", "Geschatte kost",
             "Geprojecteerde kost", "Definitieve kost", "Winst", "Marge %", "Km", "Lege km", "Status", "Afgerond"]);
        var row = 2;
        foreach (var item in rows)
        {
            Text(sheet.Cell(row, 1), item.TripNumber);
            Date(sheet.Cell(row, 2), item.TripDate);
            Text(sheet.Cell(row, 3), item.DriverName);
            Text(sheet.Cell(row, 4), item.VehicleNumber);
            Text(sheet.Cell(row, 5), item.CustomerNames);
            Money(sheet.Cell(row, 6), item.Revenue);
            Money(sheet.Cell(row, 7), item.EstimatedCost);
            Money(sheet.Cell(row, 8), item.ProjectedCost);
            MoneyOrEmpty(sheet.Cell(row, 9), item.FinalCost);
            Money(sheet.Cell(row, 10), item.Profit);
            Number1OrEmpty(sheet.Cell(row, 11), item.MarginPct);
            Number1(sheet.Cell(row, 12), item.TotalKm);
            Number1(sheet.Cell(row, 13), item.EmptyKm);
            Text(sheet.Cell(row, 14), item.Status);
            Text(sheet.Cell(row, 15), item.IsFinalized ? "Ja" : "Nee");
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildCustomerProfitabilityAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await _kpiQueryService.GetCustomerProfitabilityAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Klantrendement",
            ["Klant", "Omzet", "Toegerekende kost", "Winst", "Marge %"]);
        var row = 2;
        foreach (var item in rows)
        {
            Text(sheet.Cell(row, 1), item.CustomerName);
            Money(sheet.Cell(row, 2), item.Revenue);
            Money(sheet.Cell(row, 3), item.AllocatedCost);
            Money(sheet.Cell(row, 4), item.Profit);
            Number1OrEmpty(sheet.Cell(row, 5), item.MarginPct);
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildVehicleUtilisationAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await _kpiQueryService.GetVehicleUtilisationAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Voertuigbezetting",
            ["Voertuig", "Kenteken", "Ritten", "Uren", "Kilometers", "Bezetting %"]);
        var row = 2;
        foreach (var item in rows)
        {
            Text(sheet.Cell(row, 1), item.VehicleNumber);
            Text(sheet.Cell(row, 2), item.LicensePlate);
            sheet.Cell(row, 3).SetValue(item.TripCount);
            Number1(sheet.Cell(row, 4), item.Hours);
            Number1(sheet.Cell(row, 5), item.Km);
            Number1OrEmpty(sheet.Cell(row, 6), item.UtilisationPct);
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildDriverHoursAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await _kpiQueryService.GetDriverEffortAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Chauffeurs km-uren", ["Chauffeur", "Kilometers", "Uren"]);
        var row = 2;
        foreach (var item in rows)
        {
            Text(sheet.Cell(row, 1), item.DriverName);
            Number1(sheet.Cell(row, 2), item.Km);
            Number1(sheet.Cell(row, 3), item.Hours);
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildEmptyKmAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await _kpiQueryService.GetTripProfitabilityAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Lege kilometers",
            ["Rit", "Datum", "Chauffeur", "Totaal km", "Lege km", "Leeg %"]);
        var row = 2;
        foreach (var item in rows)
        {
            Text(sheet.Cell(row, 1), item.TripNumber);
            Date(sheet.Cell(row, 2), item.TripDate);
            Text(sheet.Cell(row, 3), item.DriverName);
            Number1(sheet.Cell(row, 4), item.TotalKm);
            Number1(sheet.Cell(row, 5), item.EmptyKm);
            Number1OrEmpty(sheet.Cell(row, 6),
                item.TotalKm > 0 ? Math.Round(item.EmptyKm / item.TotalKm * 100m, 1) : null);
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildFuelAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await FuelRowsAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Brandstof",
            ["Datum", "Voertuig", "Kenteken", "Brandstoftype", "Liters", "Bedrag", "Prijs per liter", "Station"]);
        var row = 2;
        foreach (var item in rows)
        {
            Date(sheet.Cell(row, 1), item.Date);
            Text(sheet.Cell(row, 2), item.VehicleNumber);
            Text(sheet.Cell(row, 3), item.LicensePlate);
            Text(sheet.Cell(row, 4), item.FuelType.ToString());
            Number1(sheet.Cell(row, 5), item.Litres);
            Money(sheet.Cell(row, 6), item.Amount);
            MoneyOrEmpty(sheet.Cell(row, 7), item.Litres > 0 ? Math.Round(item.Amount / item.Litres, 3) : null);
            Text(sheet.Cell(row, 8), item.Station);
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildCo2Async(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var rows = await FuelRowsAsync(filter, cancellationToken);
        var rates = await _costRateService.GetForDateAsync(filter.To, cancellationToken);
        var dieselFactor = rates?.Co2KgPerLitreDiesel ?? 2.68m;
        var otherFactor = rates?.Co2KgPerLitreOther ?? 2.31m;

        var sheet = NewSheet(workbook, "CO2",
            ["Voertuig", "Kenteken", "Brandstoftype", "Liters", "Factor (kg/l)", "CO2 (kg)"]);
        var row = 2;
        foreach (var group in rows.GroupBy(r => (r.VehicleNumber, r.LicensePlate, r.FuelType)).OrderBy(g => g.Key.VehicleNumber))
        {
            var litres = group.Sum(g => g.Litres);
            var factor = group.Key.FuelType switch
            {
                FuelType.Electric or FuelType.Hydrogen => 0m,
                FuelType.Diesel => dieselFactor,
                _ => otherFactor,
            };
            Text(sheet.Cell(row, 1), group.Key.VehicleNumber);
            Text(sheet.Cell(row, 2), group.Key.LicensePlate);
            Text(sheet.Cell(row, 3), group.Key.FuelType.ToString());
            Number1(sheet.Cell(row, 4), litres);
            sheet.Cell(row, 5).SetValue(factor).Style.NumberFormat.Format = "#,##0.000";
            Number1(sheet.Cell(row, 6), Math.Round(litres * factor, 1));
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildEtaPerformanceAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tripIds = await FilteredTripIdsAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "ETA-prestaties",
            ["Rit", "Datum", "Stop", "Type", "Venster tot", "Laatste ETA", "Aankomst", "Afwijking (min)", "Binnen venster"]);
        if (tripIds.Count == 0)
        {
            return true;
        }

        var executions = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && tripIds.Keys.Contains(e.TripId) && e.ArrivedAt != null)
            .Select(e => new { e.TripId, e.TransportOrderStopId, e.ArrivedAt })
            .ToListAsync(cancellationToken);
        var stopIds = executions.Select(e => e.TransportOrderStopId).ToList();
        var stops = stopIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && stopIds.Contains(s.Id))
                .Select(s => new { s.Id, s.StopType, s.City, s.ConfirmedTo, s.RequestedTo, s.PlannedTo })
                .ToListAsync(cancellationToken);
        var stopById = stops.ToDictionary(s => s.Id);
        var etas = await _dbContext.StopEtas.AsNoTracking()
            .Where(e => e.TenantId == tenantId && tripIds.Keys.Contains(e.TripId))
            .Select(e => new { e.Id, e.TripId, e.TransportOrderStopId })
            .ToListAsync(cancellationToken);
        var etaByStop = etas.ToDictionary(e => (e.TripId, e.TransportOrderStopId), e => e.Id);
        var etaIds = etas.Select(e => e.Id).ToList();
        var history = etaIds.Count == 0
            ? []
            : await _dbContext.StopEtaHistories.AsNoTracking()
                .Where(h => h.TenantId == tenantId && etaIds.Contains(h.StopEtaId))
                .Select(h => new { h.StopEtaId, h.Eta, h.RecordedAt })
                .ToListAsync(cancellationToken);
        var historyByEta = history.ToLookup(h => h.StopEtaId);

        var row = 2;
        foreach (var execution in executions.OrderBy(e => e.ArrivedAt))
        {
            stopById.TryGetValue(execution.TransportOrderStopId, out var stop);
            var window = stop?.ConfirmedTo ?? stop?.RequestedTo ?? stop?.PlannedTo;
            DateTime? snapshot = null;
            if (etaByStop.TryGetValue((execution.TripId, execution.TransportOrderStopId), out var etaId))
            {
                snapshot = historyByEta[etaId]
                    .Where(h => h.RecordedAt <= execution.ArrivedAt)
                    .OrderByDescending(h => h.RecordedAt)
                    .Select(h => (DateTime?)h.Eta)
                    .FirstOrDefault();
            }

            var trip = tripIds[execution.TripId];
            Text(sheet.Cell(row, 1), trip.TripNumber);
            Date(sheet.Cell(row, 2), trip.TripDate);
            Text(sheet.Cell(row, 3), stop?.City);
            Text(sheet.Cell(row, 4), stop?.StopType.ToString());
            DateTimeOrEmpty(sheet.Cell(row, 5), window);
            DateTimeOrEmpty(sheet.Cell(row, 6), snapshot);
            DateTimeOrEmpty(sheet.Cell(row, 7), execution.ArrivedAt);
            Number1OrEmpty(sheet.Cell(row, 8), snapshot is { } eta && execution.ArrivedAt is { } arrived
                ? Math.Round((decimal)(arrived - eta).TotalMinutes, 1)
                : null);
            Text(sheet.Cell(row, 9), window is null ? "—" : execution.ArrivedAt <= window ? "Ja" : "Nee");
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    private async Task<bool> BuildDeliveryReliabilityAsync(
        XLWorkbook workbook, KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tripIds = await FilteredTripIdsAsync(filter, cancellationToken);
        var sheet = NewSheet(workbook, "Leverbetrouwbaarheid",
            ["Rit", "Datum", "Geplande losstops", "Succesvol", "Mislukt", "Deellevering", "Betrouwbaarheid %"]);
        if (tripIds.Count == 0)
        {
            return true;
        }

        var orderIdsByTrip = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && tripIds.Keys.Contains(to.TripId))
            .Select(to => new { to.TripId, to.TransportOrderId })
            .ToListAsync(cancellationToken);
        var allOrderIds = orderIdsByTrip.Select(o => o.TransportOrderId).Distinct().ToList();
        var unloadingStops = allOrderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && allOrderIds.Contains(s.TransportOrderId)
                            && s.StopType == StopType.Unloading)
                .Select(s => new { s.Id, s.TransportOrderId })
                .ToListAsync(cancellationToken);
        var stopsByOrder = unloadingStops.ToLookup(s => s.TransportOrderId, s => s.Id);
        var executions = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && tripIds.Keys.Contains(e.TripId))
            .Select(e => new { e.TripId, e.TransportOrderStopId, e.Status })
            .ToListAsync(cancellationToken);
        var executionByTripStop = executions.ToLookup(e => e.TripId);

        var row = 2;
        foreach (var (tripId, trip) in tripIds.OrderBy(t => t.Value.TripDate).ThenBy(t => t.Value.TripNumber))
        {
            var tripUnloadingStops = orderIdsByTrip
                .Where(o => o.TripId == tripId)
                .SelectMany(o => stopsByOrder[o.TransportOrderId])
                .ToHashSet();
            if (tripUnloadingStops.Count == 0)
            {
                continue;
            }

            var tripExecutions = executionByTripStop[tripId]
                .Where(e => tripUnloadingStops.Contains(e.TransportOrderStopId))
                .ToList();
            var successful = tripExecutions.Count(e => e.Status == StopExecutionStatus.Completed);
            var failed = tripExecutions.Count(e => e.Status == StopExecutionStatus.Failed);
            var partial = tripExecutions.Count(e => e.Status == StopExecutionStatus.PartiallyCompleted);

            Text(sheet.Cell(row, 1), trip.TripNumber);
            Date(sheet.Cell(row, 2), trip.TripDate);
            sheet.Cell(row, 3).SetValue(tripUnloadingStops.Count);
            sheet.Cell(row, 4).SetValue(successful);
            sheet.Cell(row, 5).SetValue(failed);
            sheet.Cell(row, 6).SetValue(partial);
            Number1(sheet.Cell(row, 7), Math.Round((decimal)successful / tripUnloadingStops.Count * 100m, 1));
            row += 1;
        }

        sheet.Columns().AdjustToContents();
        return true;
    }

    // ------------------------------------------------------------- helpers

    private sealed record FuelRow(
        DateOnly Date, string VehicleNumber, string LicensePlate, FuelType FuelType,
        decimal Litres, decimal Amount, string? Station);

    private async Task<List<FuelRow>> FuelRowsAsync(KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.FuelTransactions.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.TransactionDate >= filter.From && f.TransactionDate <= filter.To);
        if (filter.VehicleId is { } vehicleId)
        {
            query = query.Where(f => f.VehicleId == vehicleId);
        }

        if (filter.DriverId is { } driverId)
        {
            query = query.Where(f => f.DriverId == driverId);
        }

        var rows = await query
            .Join(_dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == tenantId),
                f => f.VehicleId, v => v.Id,
                (f, v) => new
                {
                    f.TransactionDate, v.InternalNumber, v.LicensePlate, v.FuelType, f.Litres, f.TotalAmount, f.Station,
                })
            .OrderBy(f => f.TransactionDate)
            .ToListAsync(cancellationToken);
        return rows
            .Select(f => new FuelRow(
                f.TransactionDate, f.InternalNumber, f.LicensePlate, f.FuelType, f.Litres, f.TotalAmount, f.Station))
            .ToList();
    }

    private sealed record TripHead(string TripNumber, DateOnly TripDate);

    private async Task<Dictionary<Guid, TripHead>> FilteredTripIdsAsync(
        KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.TripDate >= filter.From && t.TripDate <= filter.To
                        && t.Status != TripStatus.Cancelled);
        if (filter.DriverId is { } driverId)
        {
            query = query.Where(t => t.DriverId == driverId);
        }

        if (filter.VehicleId is { } vehicleId)
        {
            query = query.Where(t => t.VehicleId == vehicleId);
        }

        if (filter.CustomerId is { } customerId)
        {
            query = query.Where(t => _dbContext.TripOrders
                .Any(to => to.TripId == t.Id && _dbContext.TransportOrders
                    .Any(o => o.Id == to.TransportOrderId && o.CustomerId == customerId)));
        }

        return await query
            .Take(5000)
            .Select(t => new { t.Id, t.TripNumber, t.TripDate })
            .ToDictionaryAsync(t => t.Id, t => new TripHead(t.TripNumber, t.TripDate), cancellationToken);
    }

    private async Task AddCriteriaSheetAsync(
        XLWorkbook workbook, string report, KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tenantName = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken);
        var userEmail = _currentUserContext.CurrentUserId is { } userId
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken)
            : null;
        var customerName = filter.CustomerId is { } customer
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == customer && c.TenantId == tenantId).Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var driverName = filter.DriverId is { } driver
            ? await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.Id == driver && d.TenantId == tenantId)
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var vehicleNumber = filter.VehicleId is { } vehicle
            ? await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.Id == vehicle && v.TenantId == tenantId).Select(v => v.InternalNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var sheet = workbook.AddWorksheet("Criteria");
        var rows = new (string Label, string Value)[]
        {
            ("Rapport", report),
            ("Periode van", filter.From.ToString(DateFormat)),
            ("Periode tot", filter.To.ToString(DateFormat)),
            ("Klant", customerName ?? "Alle"),
            ("Chauffeur", driverName ?? "Alle"),
            ("Voertuig", vehicleNumber ?? "Alle"),
            ("Bedrijf", tenantName ?? ""),
            ("Gegenereerd door", userEmail ?? ""),
            ("Gegenereerd op (UTC)", _timeProvider.GetUtcNow().UtcDateTime.ToString("dd-MM-yyyy HH:mm")),
        };
        for (var index = 0; index < rows.Length; index += 1)
        {
            Text(sheet.Cell(index + 1, 1), rows[index].Label);
            sheet.Cell(index + 1, 1).Style.Font.Bold = true;
            Text(sheet.Cell(index + 1, 2), rows[index].Value);
        }

        sheet.Columns().AdjustToContents();
    }

    private static IXLWorksheet NewSheet(XLWorkbook workbook, string name, string[] headers)
    {
        var sheet = workbook.AddWorksheet(name);
        for (var column = 0; column < headers.Length; column += 1)
        {
            Text(sheet.Cell(1, column + 1), headers[column]);
            sheet.Cell(1, column + 1).Style.Font.Bold = true;
        }

        sheet.SheetView.FreezeRows(1);
        return sheet;
    }

    /// <summary>Formula-injection belt-and-braces: always stored as a text VALUE, never a formula.</summary>
    private static void Text(IXLCell cell, string? value) => cell.SetValue(value ?? string.Empty);

    private static void Date(IXLCell cell, DateOnly value)
    {
        cell.SetValue(value.ToDateTime(TimeOnly.MinValue));
        cell.Style.DateFormat.Format = DateFormat;
    }

    private static void DateTimeOrEmpty(IXLCell cell, DateTime? value)
    {
        if (value is { } dateTime)
        {
            cell.SetValue(dateTime);
            cell.Style.DateFormat.Format = "dd-MM-yyyy HH:mm";
        }
    }

    private static void Money(IXLCell cell, decimal value)
    {
        cell.SetValue(value);
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void MoneyOrEmpty(IXLCell cell, decimal? value)
    {
        if (value is { } money)
        {
            Money(cell, money);
        }
    }

    private static void Number1(IXLCell cell, decimal value)
    {
        cell.SetValue(value);
        cell.Style.NumberFormat.Format = NumberFormat1;
    }

    private static void Number1OrEmpty(IXLCell cell, decimal? value)
    {
        if (value is { } number)
        {
            Number1(cell, number);
        }
    }
}
