using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Authorization;

/// <summary>
/// Requires the current user to hold AT LEAST ONE of the given permission codes (any-of).
/// Umbrella permissions (e.g. orders.manage) are expressed by listing them as an
/// alternative next to the specific code: [RequirePermission(OrdersEdit, OrdersManage)].
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    /// <summary>
    /// WH-M10 (warehouse + security acceptance 2026-10-18): request bodies were model-validated
    /// BEFORE the permission check, so a user without the right got a 400 about the body instead of
    /// a 403 (and could probe validation rules). [ApiController]'s ModelStateInvalidFilter runs at
    /// order −2000; the permission check now runs first.
    /// </summary>
    public const int FilterOrder = -3000;

    public int Order => FilterOrder;

    private readonly string[] _permissionCodes;

    public RequirePermissionAttribute(params string[] permissionCodes)
    {
        if (permissionCodes.Length == 0)
        {
            throw new ArgumentException("At least one permission code is required.", nameof(permissionCodes));
        }

        _permissionCodes = permissionCodes;
    }

    /// <summary>The any-of permission codes this endpoint accepts, in declaration order.</summary>
    public IReadOnlyList<string> PermissionCodes => _permissionCodes;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();

        if (currentUser.CurrentUserId is not { } userId)
        {
            var unauthorized = new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Authentication is required to access this resource.",
                Status = StatusCodes.Status401Unauthorized,
            };
            unauthorized.Extensions["code"] = Common.ErrorCodes.Unauthenticated;
            context.Result = new ObjectResult(unauthorized)
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IPermissionAuthorizationService>();
        var hasPermission = false;
        foreach (var code in _permissionCodes)
        {
            if (await authorizationService.UserHasPermissionAsync(userId, code, context.HttpContext.RequestAborted))
            {
                hasPermission = true;
                break;
            }
        }

        if (!hasPermission)
        {
            var forbidden = new ProblemDetails
            {
                Title = "Forbidden",
                Detail = $"Missing permission: {string.Join(" or ", _permissionCodes)}",
                Status = StatusCodes.Status403Forbidden,
            };
            forbidden.Extensions["code"] = Common.ErrorCodes.Forbidden;
            context.Result = new ObjectResult(forbidden)
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }
}
