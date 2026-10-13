using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication;

namespace TransportationService.Api.Modules.Identity.Authorization;

/// <summary>
/// Actions marked with this attribute stay reachable for an authenticated user who still owes a
/// mandatory password change (the change-password, logout and current-user endpoints).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PermitWhenPasswordChangeRequiredAttribute : Attribute;

/// <summary>
/// Per-request account-state gate applied to every authenticated request. It enforces two
/// server-side controls that a short-lived access token cannot express on its own:
/// <list type="bullet">
/// <item>Session revocation — the token's security stamp must equal the user's current stamp;
/// rotating the stamp (on an administrative password reset) invalidates all outstanding access
/// tokens immediately.</item>
/// <item>Mandatory password change — while <c>MustChangePassword</c> is set, only a small
/// allowlist of endpoints is reachable.</item>
/// </list>
/// Fail-closed: a missing/mismatched stamp or an unknown user yields 401.
/// </summary>
public sealed class AccountStateAuthorizationFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            // Anonymous endpoints and unauthenticated requests are handled by the auth policy.
            return;
        }

        if (principal.GetUserId() is not { } userId)
        {
            Deny(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Invalid token.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<TransportationDbContext>();
        var state = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp, u.MustChangePassword })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (state is null)
        {
            Deny(context, StatusCodes.Status401Unauthorized, "Unauthorized", "The account no longer exists.");
            return;
        }

        var stampClaim = principal.FindFirstValue(AppClaimTypes.SecurityStamp);
        if (!Guid.TryParse(stampClaim, out var tokenStamp) || tokenStamp != state.SecurityStamp)
        {
            Deny(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                "Your session has been revoked. Please sign in again.");
            return;
        }

        if (state.MustChangePassword && !IsPermittedDuringPasswordChange(context))
        {
            Deny(context, StatusCodes.Status403Forbidden, "Password change required",
                "You must change your password before continuing.");
        }
    }

    private static bool IsPermittedDuringPasswordChange(AuthorizationFilterContext context) =>
        context.ActionDescriptor.EndpointMetadata.OfType<PermitWhenPasswordChangeRequiredAttribute>().Any();

    private static void Deny(AuthorizationFilterContext context, int status, string title, string detail)
    {
        context.Result = new ObjectResult(new ProblemDetails { Title = title, Detail = detail, Status = status })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
