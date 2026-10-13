using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public interface IEmployeeAttentionService
{
    /// <summary>Expiring/expired documents and qualifications of one employee, for the dossier header.</summary>
    Task<EmployeeAttentionDto> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>
/// "Aandacht" snapshot for the personnel dossier: which documents and qualifications are
/// expired or about to expire. Reuses the existing expiry definitions instead of adding a
/// parallel one:
///  - documents: <see cref="ExpiryReminderPolicy"/> (kind EmployeeDocumentCategory, per category or
///    "*" wildcard) decides the lead time, default 30 days — the same policy the reminder
///    producer reads;
///  - qualifications: <see cref="IQualificationStatusCalculator"/> with the tenant's
///    QualificationExpiryWarningDays — the same status the qualifications tab shows.
/// Archived / soft-deleted documents never count; a warning stays visible exactly as long as
/// the underlying row exists in that state.
/// </summary>
public class EmployeeAttentionService : IEmployeeAttentionService
{
    public const int DefaultDocumentLeadDays = 30;
    private const int DefaultQualificationWarningDays = 30;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;

    public EmployeeAttentionService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IQualificationStatusCalculator statusCalculator, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<EmployeeAttentionDto> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var items = new List<EmployeeAttentionItemDto>();

        // --- Documents ---
        var documents = await _dbContext.EmployeeDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId && !d.IsArchived && d.ExpiryDate != null)
            .Select(d => new { d.Id, d.Category, d.CustomLabel, d.FileName, ExpiryDate = d.ExpiryDate!.Value })
            .ToListAsync(cancellationToken);

        if (documents.Count > 0)
        {
            var policies = await _dbContext.ExpiryReminderPolicies.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.IsActive && p.TargetKind == ExpiryReminderTargetKind.EmployeeDocumentCategory)
                .Select(p => new { p.TargetCode, p.LeadTimeDays })
                .ToListAsync(cancellationToken);
            var leadByCategory = policies.ToDictionary(p => p.TargetCode, p => p.LeadTimeDays, StringComparer.OrdinalIgnoreCase);
            var wildcardLead = leadByCategory.TryGetValue("*", out var wildcard) ? wildcard : DefaultDocumentLeadDays;

            foreach (var document in documents)
            {
                var lead = leadByCategory.TryGetValue(document.Category.ToString(), out var specific) ? specific : wildcardLead;
                var state = Classify(document.ExpiryDate, today, lead);
                if (state is null)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(document.CustomLabel) ? document.Category.ToString() : document.CustomLabel;
                items.Add(new EmployeeAttentionItemDto(
                    "document", document.Id, label, document.Category.ToString(), document.ExpiryDate,
                    document.ExpiryDate.DayNumber - today.DayNumber, state));
            }
        }

        // --- Qualifications ---
        var qualifications = await _dbContext.EmployeeQualifications.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.EmployeeId == employeeId && q.ExpiryDate != null)
            .ToListAsync(cancellationToken);

        if (qualifications.Count > 0)
        {
            var warningDays = await _dbContext.TenantSettings.AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Select(s => (int?)s.QualificationExpiryWarningDays)
                .FirstOrDefaultAsync(cancellationToken) ?? DefaultQualificationWarningDays;
            var typeIds = qualifications.Select(q => q.QualificationTypeId).Distinct().ToList();
            var typeNames = await _dbContext.QualificationTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

            foreach (var qualification in qualifications)
            {
                var effective = _statusCalculator.CalculateEffectiveStatus(qualification, today, warningDays);
                var state = effective switch
                {
                    QualificationStatus.Expired => "expired",
                    QualificationStatus.ExpiringSoon => "expiring",
                    _ => null,
                };
                if (state is null)
                {
                    continue;
                }

                var expiry = qualification.ExpiryDate!.Value;
                items.Add(new EmployeeAttentionItemDto(
                    "qualification", qualification.Id,
                    typeNames.GetValueOrDefault(qualification.QualificationTypeId) ?? "Kwalificatie",
                    qualification.DocumentNumber, expiry, expiry.DayNumber - today.DayNumber, state));
            }
        }

        var ordered = items
            .OrderBy(i => i.State == "expired" ? 0 : 1)
            .ThenBy(i => i.ExpiryDate)
            .ToList();

        return new EmployeeAttentionDto(
            ordered,
            DocumentsExpiring: ordered.Count(i => i.Kind == "document" && i.State == "expiring"),
            DocumentsExpired: ordered.Count(i => i.Kind == "document" && i.State == "expired"),
            QualificationsExpiring: ordered.Count(i => i.Kind == "qualification" && i.State == "expiring"),
            QualificationsExpired: ordered.Count(i => i.Kind == "qualification" && i.State == "expired"));
    }

    /// <summary>Same window semantics as the notification producer: past = expired, within the lead time = expiring.</summary>
    private static string? Classify(DateOnly expiryDate, DateOnly today, int leadDays)
    {
        if (expiryDate < today)
        {
            return "expired";
        }

        return expiryDate <= today.AddDays(leadDays) ? "expiring" : null;
    }
}
