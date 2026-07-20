namespace TransportationService.Api.Modules.Identity.Entities;

public enum UserSecurityTokenKind
{
    Activation,
    PasswordReset,
}

/// <summary>
/// Single-use, expiring security token for account activation and password reset. Only the
/// SHA-256 hash is stored — the raw value exists once, in the response/mail that carries it.
/// </summary>
public class UserSecurityToken
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    public UserSecurityTokenKind Kind { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsUsable(DateTime nowUtc) => UsedAt is null && RevokedAt is null && ExpiresAt > nowUtc;
}

/// <summary>
/// Configurable HR-function → application-role mapping used to SUGGEST roles when an account
/// is created from an employee. Suggestions are always confirmed by the administrator;
/// function changes never silently add or remove permissions.
/// </summary>
public class JobFunctionRoleMapping
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JobFunctionId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime CreatedAt { get; set; }
}
