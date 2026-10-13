using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>What kind of expiring thing a reminder policy targets.</summary>
public enum ExpiryReminderTargetKind
{
    QualificationType,
    FleetDocumentType,
    EmployeeDocumentCategory,
    TachographCalibration,
}

/// <summary>
/// Configurable reminder policy for an expiring document/qualification kind: how many days
/// before expiry to warn, who is notified, and whether e-mail goes out. One shared
/// architecture instead of hardcoding each document type. TargetCode is the qualification
/// type code / fleet document type name / employee document category name / "*" (wildcard).
/// </summary>
public class ExpiryReminderPolicy : AuditableTenantEntity
{
    public ExpiryReminderTargetKind TargetKind { get; set; }
    public string TargetCode { get; set; } = "*";

    public int LeadTimeDays { get; set; } = 30;
    /// <summary>Null = notify once per expiry window; otherwise repeat every N days within the window.</summary>
    public int? RepeatIntervalDays { get; set; }

    public bool NotifyEmployee { get; set; } = true;
    public bool NotifyHr { get; set; } = true;
    public bool NotifyPlanner { get; set; }
    public bool NotifyFleetManager { get; set; }
    public bool EmailEnabled { get; set; }

    public bool IsActive { get; set; } = true;
}
