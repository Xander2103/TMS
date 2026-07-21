using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>A named contact person at a <see cref="Customer"/>.</summary>
public class CustomerContact : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Optional display name overriding "FirstName LastName".</summary>
    public string? DisplayName { get; set; }

    /// <summary>Nickname / roepnaam.</summary>
    public string? Nickname { get; set; }

    /// <summary>Job title ("Functie"). Historic column name kept for continuity.</summary>
    public string? Role { get; set; }

    /// <summary>Department at the customer, from the tenant-managed ContactDepartment lookup.</summary>
    public Guid? DepartmentId { get; set; }

    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MobilePhone { get; set; }
    public string? PreferredLanguageCode { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
