using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>
/// Self-service urenregistratie. Elk endpoint is self-scoped via de employee-link van
/// de ingelogde gebruiker (PortalService-patroon) — er wordt NOOIT een client-supplied
/// employeeId aanvaard (spec §9). Gegate met attendance.self (leave_balances.view_own-
/// precedent voor permissie-gegate self-service).
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/attendance")]
public class MyAttendanceController : ControllerBase
{
    private const int MaxHistoryDays = 92;

    private readonly TransportationDbContext _dbContext;
    private readonly IAttendanceService _attendanceService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ITenantContext _tenantContext;

    public MyAttendanceController(
        TransportationDbContext dbContext,
        IAttendanceService attendanceService,
        ICurrentUserContext currentUserContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _attendanceService = attendanceService;
        _currentUserContext = currentUserContext;
        _tenantContext = tenantContext;
    }

    [HttpGet("status")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public async Task<ActionResult<AttendanceStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return NoEmployeeLink();
        }

        return Ok(await _attendanceService.GetStatusAsync(employeeId, cancellationToken));
    }

    [HttpPost("clock-in")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public Task<ActionResult<AttendanceStatusDto>> ClockIn(CancellationToken cancellationToken) =>
        PunchAsync((id, ctx, ct) => _attendanceService.ClockInAsync(id, ctx, ct), cancellationToken);

    [HttpPost("clock-out")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public Task<ActionResult<AttendanceStatusDto>> ClockOut(CancellationToken cancellationToken) =>
        PunchAsync((id, ctx, ct) => _attendanceService.ClockOutAsync(id, ctx, ct), cancellationToken);

    [HttpPost("break/start")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public Task<ActionResult<AttendanceStatusDto>> StartBreak(CancellationToken cancellationToken) =>
        PunchAsync((id, ctx, ct) => _attendanceService.StartBreakAsync(id, ctx, ct), cancellationToken);

    [HttpPost("break/end")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public Task<ActionResult<AttendanceStatusDto>> EndBreak(CancellationToken cancellationToken) =>
        PunchAsync((id, ctx, ct) => _attendanceService.EndBreakAsync(id, ctx, ct), cancellationToken);

    [HttpGet("history")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public async Task<ActionResult<AttendanceHistoryDto>> GetHistory(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return NoEmployeeLink();
        }

        // Fallback-default: UTC-morgen als bovengrens zodat "vandaag" in élke
        // tenant-tijdzone binnen het venster valt (de UI stuurt normaal expliciete data).
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var effectiveFrom = from ?? effectiveTo.AddDays(-14);
        if (effectiveFrom > effectiveTo || effectiveTo.DayNumber - effectiveFrom.DayNumber > MaxHistoryDays)
        {
            return BadRequest(new { message = $"De periode is ongeldig of langer dan {MaxHistoryDays} dagen." });
        }

        return Ok(await _attendanceService.GetHistoryAsync(employeeId, effectiveFrom, effectiveTo, cancellationToken));
    }

    [HttpGet("driver-day")]
    [RequirePermission(PermissionCodes.AttendanceSelf)]
    public async Task<ActionResult<DriverDaySummaryDto>> GetDriverDay(CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return NoEmployeeLink();
        }

        return Ok(await _attendanceService.GetDriverDaySummaryAsync(employeeId, cancellationToken));
    }

    private async Task<ActionResult<AttendanceStatusDto>> PunchAsync(
        Func<Guid, AttendancePunchContext, CancellationToken, Task<AttendancePunchResult>> action,
        CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return NoEmployeeLink();
        }

        var result = await action(employeeId, new AttendancePunchContext(AttendanceSource.Web), cancellationToken);
        return result.Outcome == AttendancePunchOutcome.Success
            ? Ok(result.Status)
            : Conflict(new { message = result.Error, outcome = result.Outcome.ToString() });
    }

    /// <summary>De ene employee-resolutie: via de employee-link van de ingelogde gebruiker.</summary>
    private async Task<Guid?> MyEmployeeIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private NotFoundObjectResult NoEmployeeLink() =>
        NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." });
}
