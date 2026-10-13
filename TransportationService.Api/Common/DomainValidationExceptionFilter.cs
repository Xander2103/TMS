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

        var problem = new ProblemDetails
        {
            Title = "Validatiefout",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest,
        };
        // Machine-leesbaar naast de Nederlandse detail-tekst (i18n-wave): generiek
        // common.validation_failed; specifiekere codes kunnen per throw-site volgen.
        problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        if (exception.FieldErrors is { Count: > 0 })
        {
            // Same top-level "errors" shape as ASP.NET's ValidationProblemDetails, so the
            // frontend has a single mapping path for field-level messages.
            problem.Extensions["errors"] = exception.FieldErrors;
        }

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
        context.ExceptionHandled = true;
    }
}
