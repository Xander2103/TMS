namespace TransportationService.Api.Modules.Identity.Dtos;

/// <param name="IsActivated">HR acceptance H7: false until the person has chosen a password via the
/// one-time activation link — such an account cannot log in yet, whatever IsActive says.</param>
public record UserDto(Guid Id, string Email, string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId, bool IsActive, bool IsBlocked, IReadOnlyList<RoleSummaryDto> Roles, bool IsActivated = true);

public record RoleSummaryDto(Guid Id, string Name);

public record CreateUserRequest(string Email, string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId, IReadOnlyList<Guid> RoleIds);

public record UpdateUserRequest(string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId);

public record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);
