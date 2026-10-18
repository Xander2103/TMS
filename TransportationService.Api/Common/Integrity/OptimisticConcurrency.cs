using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace TransportationService.Api.Common.Integrity;

/// <summary>
/// MD-M2 (master-data acceptance 2026-10-18): two users editing the same customer silently
/// overwrote each other (last write wins). High-risk master/configuration records carry an int
/// <see cref="Version"/> (EF concurrency token): an edit names the version it was based on, a stale
/// one is refused with 409 instead of erasing the other user's change, and the token also catches
/// the race between the check and the save. Same contract as tasks/attendance (<c>ExpectedVersion</c>).
/// </summary>
public interface IVersionedEntity
{
    int Version { get; set; }
}

public static class OptimisticConcurrency
{
    public const string ConflictMessage =
        "Deze gegevens zijn intussen gewijzigd door een andere gebruiker. Herlaad de gegevens en voer je wijziging opnieuw uit.";

    /// <summary>
    /// Refuses a stale edit and bumps the version for this save. <paramref name="expectedVersion"/>
    /// null = a caller that does not take part in the check (scripts, older clients); the token
    /// still protects against a concurrent save.
    /// </summary>
    public static void Check(IVersionedEntity entity, int? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != entity.Version)
        {
            throw new ConcurrencyConflictException();
        }

        entity.Version++;
    }
}

/// <summary>
/// A stale edit. The message defaults to the generic master-data reload text; a domain may pass a
/// more specific Dutch sentence (WH-M3: "De voorraad is intussen gewijzigd. Controleer opnieuw.").
/// </summary>
public sealed class ConcurrencyConflictException(string? message = null)
    : Exception(message ?? OptimisticConcurrency.ConflictMessage);

/// <summary>Stale edit (explicit or caught by the token at save) → 409 with the Dutch reload message.</summary>
public sealed class ConcurrencyConflictExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not (ConcurrencyConflictException or DbUpdateConcurrencyException))
        {
            return;
        }

        context.Result = new ConflictObjectResult(new
        {
            message = context.Exception is ConcurrencyConflictException conflict
                ? conflict.Message
                : OptimisticConcurrency.ConflictMessage,
            code = ErrorCodes.ConcurrencyConflict,
        });
        context.ExceptionHandled = true;
    }
}
