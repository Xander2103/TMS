namespace TransportationService.Api.Common;

/// <summary>
/// Thrown by services when a request value fails domain validation (invalid VAT number,
/// unknown country code, malformed IBAN, ...). Translated into a 400 ProblemDetails by
/// <see cref="DomainValidationExceptionFilter"/> so controllers need no per-field plumbing.
/// The message is user-facing (Dutch). When the error concerns a specific request field,
/// use the (field, message) overload: the field path (camelCase, matching the request DTO,
/// e.g. "vatNumber" or "stops[0].city") is emitted in the ProblemDetails "errors"
/// dictionary so the frontend can show the message next to the input.
/// </summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(string message) : base(message)
    {
    }

    public DomainValidationException(string field, string message) : base(message)
    {
        FieldErrors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public DomainValidationException(string message, IReadOnlyDictionary<string, string[]> fieldErrors) : base(message)
    {
        FieldErrors = fieldErrors;
    }

    /// <summary>Field path (camelCase request DTO path) → messages. Null for form-wide errors.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }
}
