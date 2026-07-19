using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TransportationService.Api.Common;

/// <summary>Translates <see cref="DomainValidationException"/> into a 400 ProblemDetails response.</summary>
public sealed class DomainValidationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainValidationException exception)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = "Validatiefout",
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
