using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TransportationService.Api.Modules.Employees.Services;

namespace TransportationService.Api.Common;

/// <summary>
/// Translates <see cref="NegativeStockConfirmationRequiredException"/> into a 409
/// ProblemDetails carrying the confirmation payload (current stock, projection, fresh
/// Version token) the frontend needs for the confirmation modal.
/// </summary>
public sealed class NegativeStockConfirmationFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not NegativeStockConfirmationRequiredException exception)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Title = "Bevestiging vereist",
            Detail = exception.VersionMismatch
                ? "De voorraad is intussen gewijzigd. Controleer de nieuwe stand en bevestig opnieuw."
                : exception.Message,
            Status = StatusCodes.Status409Conflict,
        };
        problem.Extensions["code"] = "negative_stock_confirmation_required";
        problem.Extensions["templateId"] = exception.TemplateId;
        problem.Extensions["variantId"] = exception.VariantId;
        problem.Extensions["itemName"] = exception.ItemName;
        problem.Extensions["variantLabel"] = exception.VariantLabel;
        problem.Extensions["currentStock"] = exception.CurrentStock;
        problem.Extensions["requestedDelta"] = exception.RequestedDelta;
        problem.Extensions["projectedStock"] = exception.ProjectedStock;
        problem.Extensions["version"] = exception.Version;
        problem.Extensions["requiresReason"] = exception.RequiresReason;
        problem.Extensions["versionMismatch"] = exception.VersionMismatch;

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status409Conflict,
            ContentTypes = { "application/problem+json" },
        };
        context.ExceptionHandled = true;
    }
}
