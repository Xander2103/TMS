namespace TransportationService.Api.Modules.Identity.Entities;

public class Role
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Stable identity of the seeded default-role template this role originated from
    /// (e.g. "planner"). Null for tenant-created roles. Display names may be freely
    /// renamed by tenants; template upgrades match on this code, NEVER on the name.
    /// </summary>
    public string? TemplateCode { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Tracks which default-role template version has been applied per tenant, so newly
/// introduced default permissions reach existing databases exactly once — later tenant
/// customisation (including removing an upgraded permission again) is never overridden.
/// </summary>
public class RoleTemplateState
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int AppliedVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
}
