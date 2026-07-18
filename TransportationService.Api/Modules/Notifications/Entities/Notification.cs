using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Notifications.Entities;

/// <summary>
/// In-app notification for one user. Email/SMS/push delivery is out of scope — the type code
/// is the extension point for external channels later.
/// </summary>
public class Notification : AuditableTenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>Machine-readable kind, e.g. absence_requested, absence_decided, trip_assigned.</summary>
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>SPA route to open when clicked (e.g. /planning/{id}).</summary>
    public string? LinkPath { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
