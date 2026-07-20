namespace TransportationService.Api.Modules.Identity.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? CustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }

    /// <summary>Forces a password change at the next login (temporary/admin-set credentials).</summary>
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
