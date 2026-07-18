using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>A named contact person at a <see cref="Customer"/>.</summary>
public class CustomerContact : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
}
