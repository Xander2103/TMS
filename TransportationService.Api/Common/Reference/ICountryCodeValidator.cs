namespace TransportationService.Api.Common.Reference;

/// <summary>
/// Validates ISO alpha-2 country codes against the global country reference.
/// Used by services that persist country codes (customers, locations, employees, settings).
/// </summary>
public interface ICountryCodeValidator
{
    /// <summary>
    /// Normalises the code (trim + uppercase) and verifies it exists in the global country list.
    /// Returns the normalised code, or null when the input is null/whitespace.
    /// Throws <see cref="DomainValidationException"/> for unknown codes; when a request
    /// field path is given the error is bound to that field for field-level display.
    /// </summary>
    Task<string?> NormalizeAndValidateAsync(string? code, string fieldLabel, CancellationToken cancellationToken, string? field = null);
}
