using ClosedXML.Excel;
using TransportationService.Api.Modules.Profitability.Dtos;

namespace TransportationService.Api.Modules.Profitability.Services;

public interface IProfitabilityExportService
{
    Task<(byte[] Content, string FileName)> BuildAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}

/// <summary>XLSX export of the trip profitability table (same numbers as the screen).</summary>
public class ProfitabilityExportService : IProfitabilityExportService
{
    private readonly IProfitabilityQueryService _queryService;

    public ProfitabilityExportService(IProfitabilityQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<(byte[] Content, string FileName)> BuildAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var overview = await _queryService.GetOverviewAsync(from, to, null, cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Rendement");

        string[] headers =
        [
            "Rit", "Datum", "Status", "Chauffeur", "Voertuig", "Klant(en)", "Opdrachten", "Stops", "Colli",
            "Afstand (km)", "Afstand werkelijk?", "Omzet afgesproken", "Omzet gefactureerd", "Omzet betaald",
            "Omzet gebruikt", "Omzetbron", "Kost werkelijk", "Kost raming", "Kost geprojecteerd",
            "Ontbrekende kostensoorten", "Marge", "Marge %", "Omzet/km", "Kost/km", "Afgerond?",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var trip in overview.Trips)
        {
            var c = 1;
            sheet.Cell(row, c++).Value = trip.TripNumber;
            sheet.Cell(row, c++).Value = trip.TripDate.ToString("yyyy-MM-dd");
            sheet.Cell(row, c++).Value = trip.Status.ToString();
            sheet.Cell(row, c++).Value = trip.DriverName;
            sheet.Cell(row, c++).Value = trip.VehicleNumber;
            sheet.Cell(row, c++).Value = trip.CustomerSummary;
            sheet.Cell(row, c++).Value = trip.OrderCount;
            sheet.Cell(row, c++).Value = trip.StopCount;
            sheet.Cell(row, c++).Value = trip.PackageCount;
            sheet.Cell(row, c++).Value = trip.DistanceKm ?? 0;
            sheet.Cell(row, c++).Value = trip.DistanceIsActual ? "Ja" : "Nee";
            sheet.Cell(row, c++).Value = trip.AgreedRevenue;
            sheet.Cell(row, c++).Value = trip.InvoicedRevenue;
            sheet.Cell(row, c++).Value = trip.PaidRevenue;
            sheet.Cell(row, c++).Value = trip.RevenueUsed;
            sheet.Cell(row, c++).Value = trip.RevenueSourceUsed.ToString();
            sheet.Cell(row, c++).Value = trip.ActualCost;
            sheet.Cell(row, c++).Value = trip.EstimatedCost;
            sheet.Cell(row, c++).Value = trip.ProjectedCost;
            sheet.Cell(row, c++).Value = string.Join(", ", trip.MissingCostTypes);
            sheet.Cell(row, c++).Value = trip.Margin;
            sheet.Cell(row, c++).Value = trip.MarginPct ?? 0;
            sheet.Cell(row, c++).Value = trip.RevenuePerKm ?? 0;
            sheet.Cell(row, c++).Value = trip.CostPerKm ?? 0;
            sheet.Cell(row, c).Value = trip.IsFinalized ? "Ja" : "Nee";
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"rendement-{overview.From:yyyyMMdd}-{overview.To:yyyyMMdd}.xlsx");
    }
}
