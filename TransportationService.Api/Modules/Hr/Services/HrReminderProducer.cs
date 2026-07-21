using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;

namespace TransportationService.Api.Modules.Hr.Services;

/// <summary>
/// Produces HR lifecycle reminders (birthday, seniority milestones, employment end) for a
/// tenant. Dependency-free (no ITenantContext) so the hosted sweep can run every tenant.
/// De-duplication is authoritative via <see cref="ReminderDispatchLog"/>: a reminder is only
/// created when its dedupe key has not been logged, so repeated sweeps never re-send.
/// In-app notifications go to configured role holders / the employee; e-mails go through the
/// existing outbox (idempotency key = dedupe key).
/// </summary>
public class HrReminderProducer
{
    private static readonly int[] DefaultMilestones = [1, 10, 15, 20, 25, 30];

    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public HrReminderProducer(TransportationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task ProduceForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.HrReminderSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken) ?? new HrReminderSettings();

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var sentKeys = await LoadSentKeysAsync(tenantId, cancellationToken);
        var added = false;

        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive)
            .Select(e => new EmployeeRow(e.Id, e.FirstName, e.LastName, e.DateOfBirth, e.EmploymentStartDate, e.EmploymentEndDate, e.Email))
            .ToListAsync(cancellationToken);

        if (settings.BirthdayEnabled)
        {
            added |= await ProduceBirthdaysAsync(tenantId, settings, today, employees, sentKeys, cancellationToken);
        }

        if (settings.SeniorityEnabled)
        {
            added |= await ProduceSeniorityAsync(tenantId, settings, today, employees, sentKeys, cancellationToken);
        }

        if (settings.EmploymentEndEnabled)
        {
            added |= await ProduceEmploymentEndAsync(tenantId, settings, today, employees, sentKeys, cancellationToken);
        }

        if (added)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> ProduceBirthdaysAsync(
        Guid tenantId, HrReminderSettings settings, DateOnly today, IReadOnlyList<EmployeeRow> employees,
        HashSet<string> sentKeys, CancellationToken cancellationToken)
    {
        var target = today.AddDays(settings.BirthdayDaysBefore);
        var recipients = await ResolveRoleRecipientsAsync(tenantId, ParseCsv(settings.BirthdayRecipientRoleCodes), cancellationToken);
        var added = false;

        foreach (var employee in employees)
        {
            if (employee.DateOfBirth.Month != target.Month || target.Day != EffectiveBirthDay(employee.DateOfBirth, target))
            {
                continue;
            }

            var key = $"birthday:{employee.Id}:{today.Year}";
            if (!Claim(tenantId, sentKeys, key, "birthday"))
            {
                continue;
            }

            var turning = target.Year - employee.DateOfBirth.Year;
            foreach (var recipient in recipients)
            {
                _dbContext.Add(BuildNotification(tenantId, recipient, "hr_birthday",
                    "Verjaardag medewerker",
                    $"{employee.FirstName} {employee.LastName} wordt {turning} op {target:dd-MM}.",
                    $"/employees/{employee.Id}"));
            }

            if (settings.BirthdayEmailEnabled && !string.IsNullOrWhiteSpace(employee.Email))
            {
                _dbContext.Add(BuildEmail(tenantId, employee, MessageKinds.HrBirthday, key,
                    "Gefeliciteerd!", $"Beste {employee.FirstName}, van harte gefeliciteerd met je verjaardag."));
            }

            added = true;
        }

        return added;
    }

    private async Task<bool> ProduceSeniorityAsync(
        Guid tenantId, HrReminderSettings settings, DateOnly today, IReadOnlyList<EmployeeRow> employees,
        HashSet<string> sentKeys, CancellationToken cancellationToken)
    {
        var milestones = ParseMilestones(settings.SeniorityMilestoneYears);
        var hrRecipients = await ResolveRoleRecipientsAsync(tenantId, ["hr"], cancellationToken);
        var added = false;

        foreach (var employee in employees)
        {
            foreach (var milestone in milestones)
            {
                var milestoneDate = employee.EmploymentStartDate.AddYears(milestone);

                // HR warning: SeniorityWarningDays before the milestone.
                if (today == milestoneDate.AddDays(-settings.SeniorityWarningDays))
                {
                    var warnKey = $"seniority-warn:{employee.Id}:{milestone}";
                    if (Claim(tenantId, sentKeys, warnKey, "seniority-warn"))
                    {
                        foreach (var recipient in hrRecipients)
                        {
                            _dbContext.Add(BuildNotification(tenantId, recipient, "hr_seniority",
                                "Anciënniteit nadert",
                                $"{employee.FirstName} {employee.LastName} bereikt op {milestoneDate:dd-MM-yyyy} {milestone} jaar dienst.",
                                $"/employees/{employee.Id}"));
                        }

                        added = true;
                    }
                }

                // Employee e-mail on the milestone date itself.
                if (today == milestoneDate && settings.SeniorityEmployeeEmailEnabled && !string.IsNullOrWhiteSpace(employee.Email))
                {
                    var mailKey = $"seniority-mail:{employee.Id}:{milestone}";
                    if (Claim(tenantId, sentKeys, mailKey, "seniority-mail"))
                    {
                        _dbContext.Add(BuildEmail(tenantId, employee, MessageKinds.HrSeniority, mailKey,
                            $"{milestone} jaar in dienst",
                            $"Beste {employee.FirstName}, vandaag ben je {milestone} jaar in dienst. Bedankt voor je inzet!"));
                        added = true;
                    }
                }
            }
        }

        return added;
    }

    private async Task<bool> ProduceEmploymentEndAsync(
        Guid tenantId, HrReminderSettings settings, DateOnly today, IReadOnlyList<EmployeeRow> employees,
        HashSet<string> sentKeys, CancellationToken cancellationToken)
    {
        var hrRecipients = await ResolveRoleRecipientsAsync(tenantId, ["hr"], cancellationToken);
        var added = false;

        foreach (var employee in employees)
        {
            if (employee.EmploymentEndDate is not { } endDate)
            {
                continue;
            }

            if (today != endDate.AddDays(-settings.EmploymentEndDaysBefore))
            {
                continue;
            }

            var key = $"employment-end:{employee.Id}:{endDate:yyyy-MM-dd}";
            if (!Claim(tenantId, sentKeys, key, "employment-end"))
            {
                continue;
            }

            foreach (var recipient in hrRecipients)
            {
                _dbContext.Add(BuildNotification(tenantId, recipient, "hr_employment_end",
                    "Einde tewerkstelling nadert",
                    $"De tewerkstelling van {employee.FirstName} {employee.LastName} eindigt op {endDate:dd-MM-yyyy}.",
                    $"/employees/{employee.Id}?tab=bedrijfsmiddelen"));
            }

            added = true;
        }

        return added;
    }

    // --- helpers ---

    private async Task<HashSet<string>> LoadSentKeysAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var keys = await _dbContext.ReminderDispatchLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .Select(l => l.DedupeKey)
            .ToListAsync(cancellationToken);
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Reserves a dedupe key: false if already sent (or claimed earlier this sweep).</summary>
    private bool Claim(Guid tenantId, HashSet<string> sentKeys, string key, string kind)
    {
        if (!sentKeys.Add(key))
        {
            return false;
        }

        _dbContext.Add(new ReminderDispatchLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DedupeKey = key,
            Kind = kind,
            SentAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
        return true;
    }

    private async Task<IReadOnlyList<Guid>> ResolveRoleRecipientsAsync(
        Guid tenantId, IReadOnlyList<string> templateCodes, CancellationToken cancellationToken)
    {
        if (templateCodes.Count == 0)
        {
            return [];
        }

        return await (from ur in _dbContext.UserRoles.AsNoTracking()
                      join u in _dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive)
                          on ur.UserId equals u.Id
                      join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive
                              && r.TemplateCode != null && templateCodes.Contains(r.TemplateCode))
                          on ur.RoleId equals r.Id
                      select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private static Notification BuildNotification(Guid tenantId, Guid userId, string type, string title, string message, string linkPath)
    {
        var (category, severity) = NotificationTypeCatalog.Resolve(type);
        return new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Category = category,
            Severity = severity,
            Title = title,
            Message = message,
            LinkPath = linkPath,
        };
    }

    private OutboxMessage BuildEmail(Guid tenantId, EmployeeRow employee, string kind, string idempotencyKey, string subject, string body) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Channel = MessageChannel.Email,
        Kind = kind,
        OwnerType = MessageOwnerType.Employee,
        OwnerId = employee.Id,
        RecipientAddress = employee.Email!,
        RecipientName = $"{employee.FirstName} {employee.LastName}",
        Language = "nl",
        Subject = subject,
        Body = body,
        Status = OutboxStatus.Pending,
        IdempotencyKey = idempotencyKey,
        RelatedEntityType = "Employee",
        RelatedEntityId = employee.Id.ToString(),
    };

    private static IReadOnlyList<string> ParseCsv(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<int> ParseMilestones(string csv)
    {
        var parsed = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : (int?)null)
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
        return parsed.Count > 0 ? parsed : DefaultMilestones;
    }

    /// <summary>Effective birthday day-of-month: 29 Feb birthdays fall on 28 Feb in non-leap years.</summary>
    private static int EffectiveBirthDay(DateOnly birthDate, DateOnly target) =>
        birthDate is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(target.Year) ? 28 : birthDate.Day;

    private sealed record EmployeeRow(
        Guid Id, string FirstName, string LastName, DateOnly DateOfBirth,
        DateOnly EmploymentStartDate, DateOnly? EmploymentEndDate, string? Email);
}
