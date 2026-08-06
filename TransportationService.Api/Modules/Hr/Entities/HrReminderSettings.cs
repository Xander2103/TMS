using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>
/// Per-tenant HR reminder configuration (one row per tenant, lazily created with defaults).
/// Drives the birthday, seniority-milestone and employment-end reminders produced by
/// <c>HrReminderProducer</c>. All periods are configurable here.
/// </summary>
public class HrReminderSettings : AuditableTenantEntity
{
    // Birthday reminder
    public bool BirthdayEnabled { get; set; } = true;
    /// <summary>Days before the birthday to notify (0 = on the day itself).</summary>
    public int BirthdayDaysBefore { get; set; }
    public bool BirthdayEmailEnabled { get; set; }
    /// <summary>CSV of role template codes that receive the in-app birthday notification.</summary>
    public string BirthdayRecipientRoleCodes { get; set; } = "hr";

    // Seniority milestones
    public bool SeniorityEnabled { get; set; } = true;
    /// <summary>CSV of milestone years (e.g. "1,10,15,20,25,30").</summary>
    public string SeniorityMilestoneYears { get; set; } = "1,10,15,20,25,30";
    /// <summary>HR is warned this many days before the milestone date.</summary>
    public int SeniorityWarningDays { get; set; } = 60;
    /// <summary>The employee receives an automatic e-mail on the milestone date.</summary>
    public bool SeniorityEmployeeEmailEnabled { get; set; } = true;

    // Employment end
    public bool EmploymentEndEnabled { get; set; } = true;
    public int EmploymentEndDaysBefore { get; set; } = 7;

    // Dossier follow-up (document/file completeness reminders)
    public bool DossierRemindersEnabled { get; set; } = true;
    /// <summary>Days after a dossier item becomes due before HR is reminded.</summary>
    public int DossierReminderDays { get; set; } = 7;
    /// <summary>Days after a dossier item becomes due before it is escalated. Must exceed <see cref="DossierReminderDays"/>.</summary>
    public int DossierEscalationDays { get; set; } = 30;
}
