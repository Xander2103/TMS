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

public enum RoleOperationOutcome { Success, NotFound, SystemRoleProtected }

public record RoleOperationResult(RoleOperationOutcome Outcome, RoleDto? Role);
