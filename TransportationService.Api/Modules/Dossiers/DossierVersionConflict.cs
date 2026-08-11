using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TransportationService.Api.Modules.Dossiers.Dtos;

namespace TransportationService.Api.Modules.Dossiers;

/// <summary>
/// Stale concurrency token on a dossier mutation. Carries the CURRENT state so the 409
/// response lets the client rebase instead of guessing (Trip pattern).
/// </summary>
public class DossierVersionConflictException : Exception
{
    public DossierDetailDto Current { get; }

    public DossierVersionConflictException(DossierDetailDto current)
        : base("Dit dossier is intussen door iemand anders gewijzigd. Herlaad en probeer opnieuw.")
    {
        Current = current;
    }
}

/// <summary>Maps the conflict exception onto HTTP 409 with the current dossier as body.</summary>
public class DossierVersionConflictExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is DossierVersionConflictException conflict)
        {
            context.Result = new ConflictObjectResult(conflict.Current);
            context.ExceptionHandled = true;
        }
    }
}
