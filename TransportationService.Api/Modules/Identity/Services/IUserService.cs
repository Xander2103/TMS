using TransportationService.Api.Modules.Identity.Dtos;

namespace TransportationService.Api.Modules.Identity.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken);
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserDto?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<UserOperationResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
    Task<UserOperationResult> SetBlockedAsync(Guid id, bool isBlocked, CancellationToken cancellationToken);
    Task<UserOperationResult> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken cancellationToken);

    /// <summary>Administrative password (re)set; enables portal logins for provisioned employees.</summary>
    Task<UserOperationResult> SetPasswordAsync(Guid id, string password, CancellationToken cancellationToken);
}

public enum UserOperationOutcome { Success, NotFound, LastActiveAdministrator, ValidationFailed, Forbidden }

public record UserOperationResult(UserOperationOutcome Outcome, UserDto? User, string? Error = null);
