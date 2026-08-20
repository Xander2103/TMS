using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Security;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceSettingsService
{
    Task<AttendanceSettingsDto> GetAsync(CancellationToken cancellationToken);
    Task<AttendanceSettingsDto> UpdateAsync(UpdateAttendanceSettingsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Per-tenant urenregistratie-instellingen (HrReminderSettings-patroon: één rij, lui
/// aangemaakt met defaults, waardes geclampt naar veilige grenzen). Wijzigingen worden
/// geauditeerd; KioskConfigured verklapt alleen óf de server-pepper aanwezig is, nooit
/// iets over de waarde.
/// </summary>
public class AttendanceSettingsService : IAttendanceSettingsService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAttendancePinHasher _pinHasher;
    private readonly IAuditService _auditService;

    public AttendanceSettingsService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAttendancePinHasher pinHasher,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _pinHasher = pinHasher;
        _auditService = auditService;
    }

    public async Task<AttendanceSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
        return ToDto(settings ?? new AttendanceSettings());
    }

    public async Task<AttendanceSettingsDto> UpdateAsync(
        UpdateAttendanceSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
        if (settings is null)
        {
            settings = new AttendanceSettings { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
            _dbContext.AttendanceSettings.Add(settings);
        }

        var old = ToDto(settings);
        settings.SelfPunchEnabled = request.SelfPunchEnabled;
        settings.KioskEnabled = request.KioskEnabled;
        settings.PinLength = Math.Clamp(request.PinLength, 4, 8);
        settings.ForgottenClockOutAfterHours = Math.Clamp(request.ForgottenClockOutAfterHours, 8, 48);
        settings.AutoCloseEnabled = request.AutoCloseEnabled;
        settings.AutoCloseAfterHours = Math.Clamp(request.AutoCloseAfterHours, settings.ForgottenClockOutAfterHours, 72);
        settings.PlannedNotClockedInGraceMinutes = Math.Clamp(request.PlannedNotClockedInGraceMinutes, 0, 480);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = ToDto(settings);
        await _auditService.RecordAsync("AttendanceSettings", settings.Id.ToString(), "Updated",
            old, updated, cancellationToken);
        return updated;
    }

    private AttendanceSettingsDto ToDto(AttendanceSettings settings) =>
        new(settings.SelfPunchEnabled,
            settings.KioskEnabled,
            settings.PinLength,
            settings.ForgottenClockOutAfterHours,
            settings.AutoCloseEnabled,
            settings.AutoCloseAfterHours,
            settings.PlannedNotClockedInGraceMinutes,
            KioskConfigured: _pinHasher.IsConfigured);
}
