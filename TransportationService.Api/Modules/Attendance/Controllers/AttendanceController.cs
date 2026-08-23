using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>
/// HR-kant van de urenregistratie: liveoverzicht, historiek per medewerker en
/// correcties. Lezen vereist attendance.view; elke mutatie (correctie, annulering,
/// manuele sessie) vereist attendance.correct en een verplichte reden.
/// </summary>
[ApiController]
[Authorize]
public class AttendanceController : ControllerBase
{
    private const int MaxRangeDays = 92;

    private readonly IAttendanceOverviewService _overviewService;
    private readonly IAttendanceService _attendanceService;
    private readonly IAttendanceCorrectionService _correctionService;

    public AttendanceController(
        IAttendanceOverviewService overviewService,
        IAttendanceService attendanceService,
        IAttendanceCorrectionService correctionService)
    {
        _overviewService = overviewService;
        _attendanceService = attendanceService;
        _correctionService = correctionService;
    }

    [HttpGet("api/attendance/overview")]
    [RequirePermission(PermissionCodes.AttendanceView)]
    public async Task<ActionResult<AttendanceOverviewDto>> GetOverview(
        [FromQuery] DateOnly? date, [FromQuery] Guid? departmentId, [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await _overviewService.GetOverviewAsync(date, departmentId, search, cancellationToken));

    [HttpGet("api/employees/{employeeId:guid}/attendance")]
    [RequirePermission(PermissionCodes.AttendanceView)]
    public async Task<ActionResult<AttendanceHistoryDto>> GetEmployeeHistory(
        Guid employeeId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        // UTC-morgen als fallback-bovengrens: "vandaag" valt in elke tenant-tijdzone binnen het venster.
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var effectiveFrom = from ?? effectiveTo.AddDays(-14);
        if (effectiveFrom > effectiveTo || effectiveTo.DayNumber - effectiveFrom.DayNumber > MaxRangeDays)
        {
            return BadRequest(new { message = $"De periode is ongeldig of langer dan {MaxRangeDays} dagen." });
        }

        return Ok(await _attendanceService.GetHistoryAsync(employeeId, effectiveFrom, effectiveTo, cancellationToken));
    }

    [HttpPost("api/attendance/sessions/{sessionId:guid}/corrections")]
    [RequirePermission(PermissionCodes.AttendanceCorrect)]
    public async Task<ActionResult<AttendanceSessionDto>> CorrectSession(
        Guid sessionId, [FromBody] CorrectSessionRequest request, CancellationToken cancellationToken) =>
        Handle(await _correctionService.CorrectSessionAsync(sessionId, request, cancellationToken));

    [HttpPost("api/attendance/sessions/{sessionId:guid}/breaks/{breakId:guid}/corrections")]
    [RequirePermission(PermissionCodes.AttendanceCorrect)]
    public async Task<ActionResult<AttendanceSessionDto>> CorrectBreak(
        Guid sessionId, Guid breakId, [FromBody] CorrectBreakRequest request, CancellationToken cancellationToken) =>
        Handle(await _correctionService.CorrectBreakAsync(sessionId, breakId, request, cancellationToken));

    [HttpPost("api/attendance/sessions/{sessionId:guid}/cancel")]
    [RequirePermission(PermissionCodes.AttendanceCorrect)]
    public async Task<ActionResult<AttendanceSessionDto>> CancelSession(
        Guid sessionId, [FromBody] CancelSessionRequest request, CancellationToken cancellationToken) =>
        Handle(await _correctionService.CancelSessionAsync(sessionId, request, cancellationToken));

    [HttpPost("api/attendance/sessions")]
    [RequirePermission(PermissionCodes.AttendanceCorrect)]
    public async Task<ActionResult<AttendanceSessionDto>> CreateManualSession(
        [FromBody] CreateManualSessionRequest request, CancellationToken cancellationToken) =>
        Handle(await _correctionService.CreateManualSessionAsync(request, cancellationToken));

    private ActionResult<AttendanceSessionDto> Handle(AttendanceCorrectionResult result) => result.Outcome switch
    {
        AttendanceCorrectionOutcome.Success => Ok(result.Session),
        AttendanceCorrectionOutcome.NotFound =>
            NotFound(new { message = result.Error, code = Common.ErrorCodes.NotFound }),
        AttendanceCorrectionOutcome.StaleVersion =>
            Conflict(new { message = result.Error, staleVersion = true, code = Common.ErrorCodes.AttendanceStaleVersion }),
        AttendanceCorrectionOutcome.OverlapsOtherSession =>
            Conflict(new { message = result.Error, code = Common.ErrorCodes.AttendanceSessionOverlap }),
        _ => BadRequest(new { message = result.Error, code = Common.ErrorCodes.ValidationFailed }),
    };
}
