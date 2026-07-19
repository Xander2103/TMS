using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Notifications.Entities;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical,
}

public enum NotificationCategory
{
    General,
    Orders,
    Planning,
    Execution,
    Hr,
    System,
}

/// <summary>
/// In-app notification for one user. Email/SMS/push delivery goes through the messaging
/// abstraction (Wave 9); the type code plus category/severity drive filtering, preferences
/// and future channel routing.
/// </summary>
public class Notification : AuditableTenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>Machine-readable kind, e.g. absence_requested, absence_decided, trip_assigned.</summary>
    public string Type { get; set; } = string.Empty;

    public NotificationCategory Category { get; set; } = NotificationCategory.General;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>SPA route to open when clicked (e.g. /planning/{id}).</summary>
    public string? LinkPath { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

/// <summary>Per-user opt-out per category; absent row means enabled. Critical always delivers.</summary>
public class NotificationPreference : AuditableTenantEntity
{
    public Guid UserId { get; set; }
    public NotificationCategory Category { get; set; }
    public bool Enabled { get; set; } = true;
}
