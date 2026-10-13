using ClosedXML.Excel;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

public interface IEmployeePlanningExportService
{
    /// <summary>Builds the workbook for exactly the selection the grid shows (same query, same service).</summary>
    Task<(byte[] Content, string FileName)> BuildAsync(ScheduleQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Personeelsplanning-XLSX (ClosedXML). Two sheets: "Planning" is the human overview (one row
/// per employee, one column per day, cell text per entry) and "Details" the flat dataset (one
/// row per entry) for further analysis. Every cell is written as a typed value — text stays
/// text, so formula injection is structurally impossible. Dates are real Excel dates shown as
/// DD/MM/YYYY, times are 24-hour text. The grid and this export share <see cref="IShiftService"/>
/// and <see cref="ScheduleQuery"/>, so they can never disagree on who is included.
/// </summary>
public class EmployeePlanningExportService : IEmployeePlanningExportService
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PlanningSheetName = "Planning";
    public const string DetailsSheetName = "Details";

    private const string DateFormat = "dd/mm/yyyy";
    private const int MaxColumnWidth = 60;

    private static readonly string[] DayNames = ["ma", "di", "wo", "do", "vr", "za", "zo"];

    public static readonly string[] PlanningHeaders = ["Medewerker", "Personeelsnummer", "Afdeling", "Functie"];

    public static readonly string[] DetailHeaders =
    [
        "Medewerker", "Personeelsnummer", "Afdeling", "Functie", "Datum", "Type", "Omschrijving", "Status",
        "Start", "Einde", "Uren", "Locatie / rit", "Conflict",
    ];

    private readonly IShiftService _shiftService;

    public EmployeePlanningExportService(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    public async Task<(byte[] Content, string FileName)> BuildAsync(ScheduleQuery query, CancellationToken cancellationToken)
    {
        var grid = await _shiftService.GetScheduleAsync(query, cancellationToken);

        using var workbook = new XLWorkbook();
        WritePlanningSheet(workbook.Worksheets.Add(PlanningSheetName), grid);
        WriteDetailsSheet(workbook.Worksheets.Add(DetailsSheetName), grid);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"personeelsplanning_{grid.From:yyyy-MM-dd}_{grid.To:yyyy-MM-dd}.xlsx");
    }

    private static void WritePlanningSheet(IXLWorksheet sheet, ScheduleGridDto grid)
    {
        var days = new List<DateOnly>();
        for (var date = grid.From; date <= grid.To; date = date.AddDays(1))
        {
            days.Add(date);
        }

        var column = 1;
        foreach (var header in PlanningHeaders)
        {
            sheet.Cell(1, column++).Value = header;
        }

        foreach (var day in days)
        {
            sheet.Cell(1, column++).Value = $"{DayNames[((int)day.DayOfWeek + 6) % 7]} {day:dd/MM}";
        }

        var row = 2;
        foreach (var employee in grid.Rows)
        {
            sheet.Cell(row, 1).Value = employee.EmployeeName;
            sheet.Cell(row, 2).Value = employee.EmployeeNumber;
            sheet.Cell(row, 3).Value = employee.DepartmentName ?? string.Empty;
            sheet.Cell(row, 4).Value = string.Join(", ", employee.FunctionNames ?? []);
            for (var i = 0; i < days.Count; i++)
            {
                var day = employee.Days.FirstOrDefault(d => d.Date == days[i]);
                var text = day is null ? string.Empty : string.Join("\n", day.Entries.Select(CellText));
                var cell = sheet.Cell(row, PlanningHeaders.Length + 1 + i);
                cell.Value = text;
                cell.Style.Alignment.WrapText = text.Contains('\n');
            }

            row++;
        }

        FinishSheet(sheet, PlanningHeaders.Length + days.Count, row - 1, freezeColumns: 1);
    }

    private static void WriteDetailsSheet(IXLWorksheet sheet, ScheduleGridDto grid)
    {
        for (var i = 0; i < DetailHeaders.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = DetailHeaders[i];
        }

        var row = 2;
        foreach (var employee in grid.Rows)
        {
            var functions = string.Join(", ", employee.FunctionNames ?? []);
            foreach (var day in employee.Days)
            {
                foreach (var entry in day.Entries)
                {
                    sheet.Cell(row, 1).Value = employee.EmployeeName;
                    sheet.Cell(row, 2).Value = employee.EmployeeNumber;
                    sheet.Cell(row, 3).Value = employee.DepartmentName ?? string.Empty;
                    sheet.Cell(row, 4).Value = functions;
                    var dateCell = sheet.Cell(row, 5);
                    dateCell.Value = day.Date.ToDateTime(TimeOnly.MinValue);
                    dateCell.Style.DateFormat.Format = DateFormat;
                    sheet.Cell(row, 6).Value = TypeLabel(entry);
                    sheet.Cell(row, 7).Value = entry.SourceType == "Trip" ? $"Rit {entry.Label}" : entry.Label;
                    sheet.Cell(row, 8).Value = entry.StatusLabel ?? string.Empty;
                    sheet.Cell(row, 9).Value = entry.StartTime is { } start ? start.ToString("HH\\:mm") : string.Empty;
                    sheet.Cell(row, 10).Value = entry.EndTime is { } end ? end.ToString("HH\\:mm") : string.Empty;
                    if (entry.StartTime is { } s && entry.EndTime is { } e && e > s)
                    {
                        var hours = sheet.Cell(row, 11);
                        hours.Value = Math.Round((e - s).TotalHours, 2);
                        hours.Style.NumberFormat.Format = "0.00";
                    }

                    sheet.Cell(row, 12).Value = entry.WorkLocation ?? entry.VehicleSummary ?? string.Empty;
                    sheet.Cell(row, 13).Value = entry.ConflictNotes is { Count: > 0 } notes
                        ? string.Join("; ", notes)
                        : string.Empty;
                    row++;
                }
            }
        }

        FinishSheet(sheet, DetailHeaders.Length, row - 1, freezeColumns: 0);
    }

    /// <summary>Bold header, autofilter, frozen header (and employee column), bounded column widths.</summary>
    private static void FinishSheet(IXLWorksheet sheet, int columnCount, int lastRow, int freezeColumns)
    {
        sheet.Row(1).Style.Font.SetBold();
        sheet.SheetView.Freeze(1, freezeColumns);
        // An empty selection still yields a valid workbook: the filter spans the header row only.
        sheet.Range(1, 1, Math.Max(lastRow, 1), columnCount).SetAutoFilter();
        sheet.Columns(1, columnCount).AdjustToContents();
        foreach (var column in sheet.Columns(1, columnCount))
        {
            if (column.Width > MaxColumnWidth)
            {
                column.Width = MaxColumnWidth;
            }
            else if (column.Width < 10)
            {
                column.Width = 10;
            }
        }
    }

    /// <summary>Compact per-day text: "06:00–14:00 Magazijn", "Verlof (Aangevraagd)", "Rit RIT-0007 06:00–14:00".</summary>
    private static string CellText(ScheduleEntryDto entry)
    {
        var range = entry.StartTime is { } start && entry.EndTime is { } end
            ? $"{start:HH\\:mm}–{end:HH\\:mm}"
            : null;
        return entry.SourceType switch
        {
            "Shift" => string.Join(" ", new[]
            {
                range,
                entry.ShiftType is { } type && type != Entities.ShiftType.Work ? ShiftTypeLabel(type) : entry.WorkLocation,
                entry.StatusLabel is { } status && status != "Bevestigd" ? $"({status})" : null,
            }.Where(part => !string.IsNullOrWhiteSpace(part))),
            "Trip" => string.Join(" ", new[] { $"Rit {entry.Label}", range }.Where(part => !string.IsNullOrWhiteSpace(part))),
            _ => entry.StatusLabel is { } status && status != "Goedgekeurd" ? $"{entry.Label} ({status})" : entry.Label,
        };
    }

    private static string TypeLabel(ScheduleEntryDto entry) => entry.SourceType switch
    {
        "Shift" => entry.ShiftType is { } type ? $"Shift · {ShiftTypeLabel(type)}" : "Shift",
        "Trip" => "Rit",
        "Absence" => "Afwezigheid",
        "Note" => "Notitie",
        _ => entry.SourceType,
    };

    private static string ShiftTypeLabel(Entities.ShiftType type) => type switch
    {
        Entities.ShiftType.Standby => "Stand-by",
        Entities.ShiftType.Training => "Opleiding",
        _ => "Werk",
    };
}
