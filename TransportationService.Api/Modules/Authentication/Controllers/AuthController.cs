using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Modules.Authentication.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokensDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password, cancellationToken);
        return result.Outcome switch
        {
            AuthOutcome.Success => Ok(result.Tokens),
            _ => InvalidCredentials(),
        };
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokensDto>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken, cancellationToken);
        return result.Outcome switch
        {
            AuthOutcome.Success => Ok(result.Tokens),
            _ => InvalidCredentials(),
        };
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _authService.GetCurrentUserAsync(userId.Value, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    private ActionResult InvalidCredentials() =>
        Problem(
            title: "Invalid credentials",
            detail: "The email or password is incorrect, or the account is not permitted to sign in.",
            statusCode: StatusCodes.Status401Unauthorized);
}
