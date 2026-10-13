using TransportationService.Api.Modules.Identity.Dtos;

namespace TransportationService.Api.Modules.Identity.Services;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken);
    Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleOperationResult> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleOperationResult> DeactivateAsync(Guid id, CancellationToken cancellationToken);
    Task<RoleOperationResult> AssignPermissionsAsync(Guid id, AssignPermissionsRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken);
}

public enum RoleOperationOutcome { Success, NotFound, SystemRoleProtected, Forbidden, ValidationFailed }

/// <summary>Optional human-readable reason attached to a Forbidden/ValidationFailed outcome.</summary>
public static class RoleOperationMessages
{
    public const string PrivilegeEscalation = "You cannot grant permissions you do not hold yourself.";
    public const string PortalRoleInternalPermission = "Customer-portal roles may only hold customer_portal.* permissions.";
    public const string UnknownPermission = "One or more permission codes are unknown.";
}

public record RoleOperationResult(RoleOperationOutcome Outcome, RoleDto? Role, string? Error = null);
