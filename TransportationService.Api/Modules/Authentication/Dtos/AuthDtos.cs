namespace TransportationService.Api.Modules.Authentication.Dtos;

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record LogoutRequest(string? RefreshToken);

public record AuthTokensDto(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    CurrentUserDto User);

public record CurrentUserDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Email,
    string FirstName,
    string LastName,
    Guid? EmployeeId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public enum AuthOutcome
{
    Success,
    InvalidCredentials,
    Disabled,
}

public record AuthResult(AuthOutcome Outcome, AuthTokensDto? Tokens)
{
    public static AuthResult Success(AuthTokensDto tokens) => new(AuthOutcome.Success, tokens);
    public static readonly AuthResult InvalidCredentials = new(AuthOutcome.InvalidCredentials, null);
    public static readonly AuthResult Disabled = new(AuthOutcome.Disabled, null);
}
