using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _permissionCode;

    public RequirePermissionAttribute(string permissionCode)
    {
        _permissionCode = permissionCode;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();

        if (currentUser.CurrentUserId is not { } userId)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IPermissionAuthorizationService>();
        var hasPermission = await authorizationService.UserHasPermissionAsync(userId, _permissionCode, context.HttpContext.RequestAborted);

        if (!hasPermission)
        {
            context.Result = new ObjectResult(new { message = $"Missing permission: {_permissionCode}" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
