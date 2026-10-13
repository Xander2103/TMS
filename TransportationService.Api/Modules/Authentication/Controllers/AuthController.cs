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

    /// <summary>
    /// H5: the refresh token travels in an HttpOnly cookie so it is unreachable from JavaScript
    /// (XSS cannot exfiltrate the long-lived credential). Scoped to the auth endpoints only;
    /// SameSite=Strict means no cross-site request ever carries it (CSRF). The body still ACCEPTS
    /// a token for non-browser API clients, but browser responses no longer contain one.
    /// </summary>
    private const string RefreshCookieName = "ts_refresh";

    private CookieOptions RefreshCookieOptions(DateTime? expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        IsEssential = true,
        Expires = expiresAtUtc is { } expiry ? new DateTimeOffset(expiry, TimeSpan.Zero) : null,
    };

    /// <summary>Cookie set + the raw refresh token stripped from the JSON body.</summary>
    private AuthTokensDto IssueRefreshCookie(AuthTokensDto tokens)
    {
        Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, RefreshCookieOptions(tokens.RefreshTokenExpiresAt));
        return tokens with { RefreshToken = string.Empty };
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.AuthPolicy)]
    public async Task<ActionResult<AuthTokensDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password, cancellationToken);
        return result.Outcome switch
        {
            AuthOutcome.Success => Ok(IssueRefreshCookie(result.Tokens!)),
            _ => InvalidCredentials(),
        };
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.SessionPolicy)]
    public async Task<ActionResult<AuthTokensDto>> Refresh(RefreshRequest? request, CancellationToken cancellationToken)
    {
        // Cookie first (browser flow); explicit body token only for non-browser API clients.
        var fromCookie = Request.Cookies[RefreshCookieName];
        var refreshToken = !string.IsNullOrEmpty(fromCookie) ? fromCookie : request?.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
        {
            return InvalidCredentials();
        }

        var result = await _authService.RefreshAsync(refreshToken, cancellationToken);
        if (result.Outcome != AuthOutcome.Success)
        {
            Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(null));
            return InvalidCredentials();
        }

        // Rotation: the cookie always carries the newest token of the family.
        return Ok(IssueRefreshCookie(result.Tokens!));
    }

    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.SessionPolicy)]
    [TransportationService.Api.Modules.Identity.Authorization.PermitWhenPasswordChangeRequired]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        var refreshToken = request.RefreshToken ?? Request.Cookies[RefreshCookieName];
        await _authService.LogoutAsync(refreshToken, cancellationToken);
        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(null));
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
