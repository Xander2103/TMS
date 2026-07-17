namespace TransportationService.Api.Modules.Identity.Dtos;

public record UserDto(Guid Id, string Email, string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId, bool IsActive, bool IsBlocked, IReadOnlyList<RoleSummaryDto> Roles);

public record RoleSummaryDto(Guid Id, string Name);

public record CreateUserRequest(string Email, string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId, IReadOnlyList<Guid> RoleIds);

public record UpdateUserRequest(string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId);

public record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);
