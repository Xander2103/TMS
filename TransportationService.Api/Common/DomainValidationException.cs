namespace TransportationService.Api.Common;

/// <summary>
/// Thrown by services when a request value fails domain validation (invalid VAT number,
/// unknown country code, malformed IBAN, ...). Translated into a 400 ProblemDetails by
/// <see cref="DomainValidationExceptionFilter"/> so controllers need no per-field plumbing.
/// The message is user-facing (Dutch).
/// </summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(string message) : base(message)
    {
    }
}
