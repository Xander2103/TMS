using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TransportationService.Api.Common;

/// <summary>
/// Translates <see cref="InvalidTenantReferenceException"/> into a 400 ProblemDetails response,
/// so services can reject cross-tenant/unknown reference ids without every controller needing
/// its own try/catch.
/// </summary>
public sealed class InvalidTenantReferenceExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvalidTenantReferenceException exception)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = "Ongeldige referentie",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest,
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
        context.ExceptionHandled = true;
    }
}
