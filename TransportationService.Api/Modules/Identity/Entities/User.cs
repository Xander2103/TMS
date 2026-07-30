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

    /// <summary>
    /// Server-side session-revocation stamp. It is embedded in every access token and verified
    /// on each authenticated request; rotating it (on an administrative password reset, and any
    /// future block/deactivate) immediately invalidates all outstanding access tokens for this
    /// user without waiting for their short lifetime to elapse.
    /// </summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    /// <summary>Consecutive failed sign-in attempts since the last success (account lockout).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>When set and in the future, sign-in is temporarily locked for this account.</summary>
    public DateTime? LockoutEndsAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
