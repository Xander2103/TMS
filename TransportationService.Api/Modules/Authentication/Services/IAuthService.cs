using TransportationService.Api.Modules.Authentication.Dtos;

namespace TransportationService.Api.Modules.Authentication.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
