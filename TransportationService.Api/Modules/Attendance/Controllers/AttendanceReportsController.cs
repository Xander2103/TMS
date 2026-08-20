using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>
/// Urenregistratie-rapportage: JSON voor het scherm, XLSX als export (ReportCatalog
/// "attendance"). Exports van personeelstijden zijn vertrouwelijke persoonsgegevens en
/// worden als zodanig in de audit geregistreerd.
/// </summary>
[ApiController]
[Authorize]
public class AttendanceReportsController : ControllerBase
{
    private const int MaxRangeDays = 190;

    private readonly IAttendanceReportService _reportService;
    private readonly IAttendanceExportService _exportService;
    private readonly IAuditService _auditService;

    public AttendanceReportsController(
        IAttendanceReportService reportService,
        IAttendanceExportService exportService,
        IAuditService auditService)
    {
        _reportService = reportService;
        _exportService = exportService;
        _auditService = auditService;
    }

    [HttpGet("api/attendance/report")]
    [RequirePermission(PermissionCodes.AttendanceReport)]
    public async Task<ActionResult<AttendanceReportDto>> GetReport(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? employeeId, [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        if (Normalize(from, to) is not { } range)
        {
            return BadRequest(new { message = $"De periode is ongeldig of langer dan {MaxRangeDays} dagen." });
        }

        return Ok(await _reportService.BuildAsync(range.From, range.To, employeeId, departmentId, cancellationToken));
    }

    [HttpGet("api/reports/attendance")]
    [RequirePermission(PermissionCodes.AttendanceReport)]
    public async Task<IActionResult> Download(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? employeeId, [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        if (Normalize(from, to) is not { } range)
        {
            return BadRequest(new { message = $"De periode is ongeldig of langer dan {MaxRangeDays} dagen." });
        }

        var (content, fileName) = await _exportService.BuildAsync(
            range.From, range.To, employeeId, departmentId, cancellationToken);

        await _auditService.RecordExportAsync("attendance", new { range.From, range.To, employeeId, departmentId },
            cancellationToken, SecurityAuditEvents.Classification.Confidential);

        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static (DateOnly From, DateOnly To)? Normalize(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveTo = to ?? today;
        var effectiveFrom = from ?? effectiveTo.AddDays(-30);
        if (effectiveFrom > effectiveTo || effectiveTo.DayNumber - effectiveFrom.DayNumber > MaxRangeDays)
        {
            return null;
        }

        return (effectiveFrom, effectiveTo);
    }
}
