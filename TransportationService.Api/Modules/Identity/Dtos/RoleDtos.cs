namespace TransportationService.Api.Modules.Identity.Dtos;

public record RoleDto(Guid Id, string Name, string? Description, bool IsSystemRole, bool IsActive, IReadOnlyList<string> PermissionCodes);

public record CreateRoleRequest(string Name, string? Description);

public record UpdateRoleRequest(string Name, string? Description);

public record AssignPermissionsRequest(IReadOnlyList<string> PermissionCodes);

public record PermissionDto(Guid Id, string Code, string Module, string Action, string Description);
