using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.CustomerPortal.Entities;

/// <summary>
/// A broadcast notice shown to every customer-portal user within its active window. Managed by
/// staff (portal_announcements.manage); read by all portal users of the tenant (no per-customer
/// targeting in v1).
/// </summary>
public class PortalAnnouncement : AuditableTenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Null = visible immediately (subject to IsActive).</summary>
    public DateTime? ActiveFrom { get; set; }

    /// <summary>Null = visible indefinitely (subject to IsActive).</summary>
    public DateTime? ActiveUntil { get; set; }

    public bool IsActive { get; set; } = true;
}
