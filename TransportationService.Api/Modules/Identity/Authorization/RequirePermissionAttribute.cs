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
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
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
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Authentication is required to access this resource.",
                Status = StatusCodes.Status401Unauthorized,
            })
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
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Forbidden",
                Detail = $"Missing permission: {string.Join(" or ", _permissionCodes)}",
                Status = StatusCodes.Status403Forbidden,
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }
}
