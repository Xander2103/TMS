using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Authentication.Controllers;

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserAccountFlowService _accountFlows;

    public AuthController(IAuthService authService, IUserAccountFlowService accountFlows)
    {
        _authService = authService;
        _accountFlows = accountFlows;
    }

    /// <summary>Always returns 204 — the response never reveals whether an account exists.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.AuthPolicy)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _accountFlows.RequestPasswordResetAsync(request.Email, cancellationToken);
        return NoContent();
    }

    /// <summary>Completes password reset OR account activation with a single-use token (the same
    /// endpoint serves both — see UserAccountFlowService.CompleteWithTokenAsync — so the
    /// frontend's /activeren page posts here too, no separate activation endpoint needed).</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.AuthPolicy)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var error = await _accountFlows.CompleteWithTokenAsync(request.Token, request.NewPassword, cancellationToken);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.AuthPolicy)]
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
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.SessionPolicy)]
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
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.SessionPolicy)]
    [TransportationService.Api.Modules.Identity.Authorization.PermitWhenPasswordChangeRequired]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [TransportationService.Api.Modules.Identity.Authorization.PermitWhenPasswordChangeRequired]
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
