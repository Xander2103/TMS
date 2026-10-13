using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Hr.Services;

public record HrReminderSettingsDto(
    bool BirthdayEnabled, int BirthdayDaysBefore, bool BirthdayEmailEnabled, string BirthdayRecipientRoleCodes,
    bool SeniorityEnabled, string SeniorityMilestoneYears, int SeniorityWarningDays, bool SeniorityEmployeeEmailEnabled,
    bool EmploymentEndEnabled, int EmploymentEndDaysBefore,
    bool DossierRemindersEnabled, int DossierReminderDays, int DossierEscalationDays);

public record ExpiryReminderPolicyDto(
    Guid Id, ExpiryReminderTargetKind TargetKind, string TargetCode, int LeadTimeDays, int? RepeatIntervalDays,
    bool NotifyEmployee, bool NotifyHr, bool NotifyPlanner, bool NotifyFleetManager, bool EmailEnabled, bool IsActive);

public record SaveExpiryReminderPolicyRequest(
    ExpiryReminderTargetKind TargetKind, string TargetCode, int LeadTimeDays, int? RepeatIntervalDays,
    bool NotifyEmployee, bool NotifyHr, bool NotifyPlanner, bool NotifyFleetManager, bool EmailEnabled, bool IsActive);

public interface IHrReminderConfigService
{
    Task<HrReminderSettingsDto> GetSettingsAsync(CancellationToken cancellationToken);
    Task<HrReminderSettingsDto> UpdateSettingsAsync(HrReminderSettingsDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpiryReminderPolicyDto>> ListPoliciesAsync(CancellationToken cancellationToken);
    Task<ExpiryReminderPolicyDto> CreatePolicyAsync(SaveExpiryReminderPolicyRequest request, CancellationToken cancellationToken);
    Task<ExpiryReminderPolicyDto?> UpdatePolicyAsync(Guid id, SaveExpiryReminderPolicyRequest request, CancellationToken cancellationToken);
    Task<bool> DeletePolicyAsync(Guid id, CancellationToken cancellationToken);
}

public class HrReminderConfigService : IHrReminderConfigService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public HrReminderConfigService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<HrReminderSettingsDto> GetSettingsAsync(CancellationToken cancellationToken) =>
        Map(await GetOrCreateAsync(cancellationToken));

    public async Task<HrReminderSettingsDto> UpdateSettingsAsync(HrReminderSettingsDto request, CancellationToken cancellationToken)
    {
        if (request.DossierReminderDays < 1 || request.DossierReminderDays > 365)
        {
            throw new DomainValidationException("dossierReminderDays", "Kies een aantal dagen tussen 1 en 365.");
        }
        if (request.DossierEscalationDays < 1 || request.DossierEscalationDays > 365)
        {
            throw new DomainValidationException("dossierEscalationDays", "Kies een aantal dagen tussen 1 en 365.");
        }
        if (request.DossierEscalationDays <= request.DossierReminderDays)
        {
            throw new DomainValidationException("dossierEscalationDays", "Escalatie moet later vallen dan de eerste melding.");
        }

        var settings = await GetOrCreateAsync(cancellationToken);

        var before = new
        {
            settings.BirthdayEnabled, settings.SeniorityEnabled, settings.EmploymentEndEnabled,
            settings.DossierRemindersEnabled, settings.DossierReminderDays, settings.DossierEscalationDays,
        };

        settings.BirthdayEnabled = request.BirthdayEnabled;
        settings.BirthdayDaysBefore = Math.Clamp(request.BirthdayDaysBefore, 0, 60);
        settings.BirthdayEmailEnabled = request.BirthdayEmailEnabled;
        settings.BirthdayRecipientRoleCodes = NormalizeCsv(request.BirthdayRecipientRoleCodes, "hr");
        settings.SeniorityEnabled = request.SeniorityEnabled;
        settings.SeniorityMilestoneYears = NormalizeMilestones(request.SeniorityMilestoneYears);
        settings.SeniorityWarningDays = Math.Clamp(request.SeniorityWarningDays, 0, 365);
        settings.SeniorityEmployeeEmailEnabled = request.SeniorityEmployeeEmailEnabled;
        settings.EmploymentEndEnabled = request.EmploymentEndEnabled;
        settings.EmploymentEndDaysBefore = Math.Clamp(request.EmploymentEndDaysBefore, 0, 365);
        settings.DossierRemindersEnabled = request.DossierRemindersEnabled;
        settings.DossierReminderDays = request.DossierReminderDays;
        settings.DossierEscalationDays = request.DossierEscalationDays;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("HrReminderSettings", settings.TenantId.ToString(), "Updated", before,
            new
            {
                settings.BirthdayEnabled, settings.SeniorityEnabled, settings.EmploymentEndEnabled,
                settings.DossierRemindersEnabled, settings.DossierReminderDays, settings.DossierEscalationDays,
            }, cancellationToken);

        return Map(settings);
    }

    public async Task<IReadOnlyList<ExpiryReminderPolicyDto>> ListPoliciesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ExpiryReminderPolicies
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .OrderBy(p => p.TargetKind).ThenBy(p => p.TargetCode)
            .Select(p => Map(p))
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpiryReminderPolicyDto> CreatePolicyAsync(SaveExpiryReminderPolicyRequest request, CancellationToken cancellationToken)
    {
        var code = string.IsNullOrWhiteSpace(request.TargetCode) ? "*" : request.TargetCode.Trim();
        var duplicate = await _dbContext.ExpiryReminderPolicies.AnyAsync(
            p => p.TenantId == _tenantContext.TenantId && p.TargetKind == request.TargetKind && p.TargetCode == code, cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("targetCode", "Er bestaat al een herinneringsbeleid voor dit type en deze code.");
        }

        var policy = new ExpiryReminderPolicy { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, TargetCode = code };
        Apply(policy, request);
        _dbContext.ExpiryReminderPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ExpiryReminderPolicy", policy.Id.ToString(), "Created", null,
            new { policy.TargetKind, policy.TargetCode, policy.LeadTimeDays }, cancellationToken);
        return Map(policy);
    }

    public async Task<ExpiryReminderPolicyDto?> UpdatePolicyAsync(Guid id, SaveExpiryReminderPolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.ExpiryReminderPolicies
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (policy is null)
        {
            return null;
        }

        Apply(policy, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ExpiryReminderPolicy", policy.Id.ToString(), "Updated", null,
            new { policy.LeadTimeDays, policy.IsActive }, cancellationToken);
        return Map(policy);
    }

    public async Task<bool> DeletePolicyAsync(Guid id, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.ExpiryReminderPolicies
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (policy is null)
        {
            return false;
        }

        _dbContext.Remove(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ExpiryReminderPolicy", policy.Id.ToString(), "Deleted",
            new { policy.TargetKind, policy.TargetCode }, null, cancellationToken);
        return true;
    }

    private static void Apply(ExpiryReminderPolicy policy, SaveExpiryReminderPolicyRequest request)
    {
        policy.TargetKind = request.TargetKind;
        policy.TargetCode = string.IsNullOrWhiteSpace(request.TargetCode) ? "*" : request.TargetCode.Trim();
        policy.LeadTimeDays = Math.Clamp(request.LeadTimeDays, 0, 730);
        policy.RepeatIntervalDays = request.RepeatIntervalDays is { } r && r > 0 ? Math.Min(r, 365) : null;
        policy.NotifyEmployee = request.NotifyEmployee;
        policy.NotifyHr = request.NotifyHr;
        policy.NotifyPlanner = request.NotifyPlanner;
        policy.NotifyFleetManager = request.NotifyFleetManager;
        policy.EmailEnabled = request.EmailEnabled;
        policy.IsActive = request.IsActive;
    }

    private async Task<HrReminderSettings> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.HrReminderSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
        if (settings is null)
        {
            settings = new HrReminderSettings { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
            _dbContext.HrReminderSettings.Add(settings);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(settings).State = EntityState.Detached;
                settings = await _dbContext.HrReminderSettings.FirstAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
            }
        }

        return settings;
    }

    private static string NormalizeCsv(string? csv, string fallback)
    {
        var parts = (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? string.Join(',', parts) : fallback;
    }

    private static string NormalizeMilestones(string? csv)
    {
        var parts = (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var v) && v > 0 ? v : (int?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        return parts.Count > 0 ? string.Join(',', parts) : "1,10,15,20,25,30";
    }

    private static HrReminderSettingsDto Map(HrReminderSettings s) => new(
        s.BirthdayEnabled, s.BirthdayDaysBefore, s.BirthdayEmailEnabled, s.BirthdayRecipientRoleCodes,
        s.SeniorityEnabled, s.SeniorityMilestoneYears, s.SeniorityWarningDays, s.SeniorityEmployeeEmailEnabled,
        s.EmploymentEndEnabled, s.EmploymentEndDaysBefore,
        s.DossierRemindersEnabled, s.DossierReminderDays, s.DossierEscalationDays);

    private static ExpiryReminderPolicyDto Map(ExpiryReminderPolicy p) => new(
        p.Id, p.TargetKind, p.TargetCode, p.LeadTimeDays, p.RepeatIntervalDays,
        p.NotifyEmployee, p.NotifyHr, p.NotifyPlanner, p.NotifyFleetManager, p.EmailEnabled, p.IsActive);
}
