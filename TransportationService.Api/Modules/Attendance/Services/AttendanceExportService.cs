using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceExportService
{
    Task<(byte[] Content, string FileName)> BuildAsync(
        DateOnly from, DateOnly to, Guid? employeeId, Guid? departmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Urenregistratie-XLSX (ClosedXML). Elke cel wordt als getypte waarde geschreven —
/// tekst blijft tekst, dus formule-injectie is structureel onmogelijk. Elke werkmap
/// krijgt een Criteria-blad (periode, filters, gegenereerd-op, gebruiker). Duraties
/// staan als minuten (geheel getal) zodat payroll-achtige naverwerking exact blijft;
/// de kolomkoppen benoemen dat expliciet. Dit is een export van geregistreerde
/// werkelijkheid — geen loonberekening.
/// </summary>
public class AttendanceExportService : IAttendanceExportService
{
    private readonly IAttendanceReportService _reportService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly TimeProvider _timeProvider;
    private readonly TransportationDbContext _dbContext;

    public AttendanceExportService(
        IAttendanceReportService reportService,
        ICurrentUserContext currentUserContext,
        TimeProvider timeProvider,
        TransportationDbContext dbContext)
    {
        _reportService = reportService;
        _currentUserContext = currentUserContext;
        _timeProvider = timeProvider;
        _dbContext = dbContext;
    }

    public async Task<(byte[] Content, string FileName)> BuildAsync(
        DateOnly from, DateOnly to, Guid? employeeId, Guid? departmentId, CancellationToken cancellationToken)
    {
        var report = await _reportService.BuildAsync(from, to, employeeId, departmentId, cancellationToken);

        // Handmatige export: koppen in de taal van de aanvragende gebruiker; data blijft
        // in elke taal identiek (machine-to-machine-contracten worden nooit vertaald).
        var strings = AttendanceExportStrings.For(await CallerLanguageAsync(cancellationToken));

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(strings.SheetName);
        string[] headers =
        [
            strings.Employee, strings.EmployeeNumber, strings.Department, strings.Date,
            strings.GrossMinutes, strings.BreakMinutes, strings.NetMinutes, strings.PlannedMinutes,
            strings.DeviationMinutes, strings.MissingClockOut, strings.Corrections,
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        sheet.Row(1).Style.Font.SetBold();
        sheet.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var line in report.Rows)
        {
            sheet.Cell(row, 1).Value = line.EmployeeName;
            sheet.Cell(row, 2).Value = line.EmployeeNumber;
            sheet.Cell(row, 3).Value = line.DepartmentName ?? string.Empty;
            sheet.Cell(row, 4).Value = line.Date.ToString("dd-MM-yyyy");
            sheet.Cell(row, 5).Value = line.GrossMinutes;
            sheet.Cell(row, 6).Value = line.BreakMinutes;
            sheet.Cell(row, 7).Value = line.NetMinutes;
            if (line.PlannedMinutes is { } planned)
            {
                sheet.Cell(row, 8).Value = planned;
            }

            if (line.DeviationMinutes is { } deviation)
            {
                sheet.Cell(row, 9).Value = deviation;
            }

            sheet.Cell(row, 10).Value = line.MissingClockOut ? strings.Yes : string.Empty;
            sheet.Cell(row, 11).Value = line.CorrectionCount;
            row++;
        }

        // Totalenrij.
        sheet.Cell(row, 1).Value = strings.Total;
        sheet.Cell(row, 5).Value = report.TotalGrossMinutes;
        sheet.Cell(row, 6).Value = report.TotalBreakMinutes;
        sheet.Cell(row, 7).Value = report.TotalNetMinutes;
        sheet.Cell(row, 8).Value = report.TotalPlannedMinutes;
        sheet.Row(row).Style.Font.SetBold();
        sheet.Columns().AdjustToContents();

        AddCriteriaSheet(workbook, strings, from, to, employeeId, departmentId);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm");
        return (stream.ToArray(), $"urenregistratie-{stamp}.xlsx");
    }

    private void AddCriteriaSheet(
        XLWorkbook workbook, AttendanceExportStrings strings,
        DateOnly from, DateOnly to, Guid? employeeId, Guid? departmentId)
    {
        var sheet = workbook.Worksheets.Add("Criteria");
        sheet.Cell(1, 1).Value = strings.CriteriaReport;
        sheet.Cell(1, 2).Value = strings.SheetName;
        sheet.Cell(2, 1).Value = strings.CriteriaFrom;
        sheet.Cell(2, 2).Value = from.ToString("dd-MM-yyyy");
        sheet.Cell(3, 1).Value = strings.CriteriaTo;
        sheet.Cell(3, 2).Value = to.ToString("dd-MM-yyyy");
        sheet.Cell(4, 1).Value = strings.CriteriaEmployee;
        sheet.Cell(4, 2).Value = employeeId?.ToString() ?? strings.CriteriaAll;
        sheet.Cell(5, 1).Value = strings.CriteriaDepartment;
        sheet.Cell(5, 2).Value = departmentId?.ToString() ?? strings.CriteriaAll;
        sheet.Cell(6, 1).Value = strings.CriteriaGeneratedAt;
        sheet.Cell(6, 2).Value = _timeProvider.GetUtcNow().UtcDateTime.ToString("dd-MM-yyyy HH:mm") + " UTC";
        sheet.Cell(7, 1).Value = strings.CriteriaByUser;
        sheet.Cell(7, 2).Value = _currentUserContext.CurrentUserId?.ToString() ?? "";
        sheet.Column(1).Style.Font.SetBold();
        sheet.Columns().AdjustToContents();
    }

    /// <summary>Taal van de aanvrager: User.PreferredLanguageCode → nl.</summary>
    private async Task<string?> CallerLanguageAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredLanguageCode)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
