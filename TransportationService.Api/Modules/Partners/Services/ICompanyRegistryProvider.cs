namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>
/// Official company-registry lookup seam (KBO/CBE, VIES, Peppol directory, ...). The
/// application ships without a configured provider: the UI offers a manual lookup button
/// with a clear "no provider" state and validated manual entry as fallback. No unofficial
/// scraping — a real implementation must use an official API and be registered in DI.
/// </summary>
public interface ICompanyRegistryProvider
{
    bool IsConfigured { get; }

    /// <summary>Null when nothing was found (or no provider is configured).</summary>
    Task<CompanyRegistryResult?> LookupAsync(string vatOrCompanyNumber, CancellationToken cancellationToken);
}

public sealed record CompanyRegistryResult(
    string? LegalName,
    string? CompanyNumber,
    string? VatNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? PeppolId,
    string? PeppolScheme);

/// <summary>Default no-op provider: reports the not-configured state.</summary>
public sealed class NullCompanyRegistryProvider : ICompanyRegistryProvider
{
    public bool IsConfigured => false;

    public Task<CompanyRegistryResult?> LookupAsync(string vatOrCompanyNumber, CancellationToken cancellationToken) =>
        Task.FromResult<CompanyRegistryResult?>(null);
}
