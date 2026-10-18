using TransportationService.Api.Modules.Authentication.Dtos;

namespace TransportationService.Api.Modules.Authentication.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Ends one sign-in session: the presented refresh token's rotation family and the session the
    /// caller's access token belongs to. The user's other sessions stay signed in.
    /// </summary>
    Task LogoutAsync(string? refreshToken, Guid? userId, Guid? sessionId, CancellationToken cancellationToken);

    Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
