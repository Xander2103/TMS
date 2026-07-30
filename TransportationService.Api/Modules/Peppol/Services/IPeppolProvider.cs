namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>Result of checking whether a Peppol participant (scheme:id) is registered and what it supports.</summary>
public sealed record PeppolParticipantResult(bool Found, IReadOnlyList<string> SupportedDocumentTypes, string? ProviderReference);

/// <summary>Everything a provider needs to submit one outbound document.</summary>
public sealed record PeppolOutboundRequest(
    string SellerScheme,
    string SellerId,
    string BuyerScheme,
    string BuyerId,
    string DocumentXml,
    string PayloadHash,
    Guid CorrelationId);

/// <summary>Immediate provider acknowledgement of a submitted document.</summary>
public sealed record PeppolSubmissionResult(bool Accepted, string? ProviderMessageId, string? Error);

/// <summary>Point-in-time transmission status as reported by the provider.</summary>
public sealed record PeppolTransmissionStatusResult(string Status, string? Detail);

/// <summary>Structural/semantic validation result for a rendered document.</summary>
public sealed record PeppolDocumentValidationResult(bool Valid, IReadOnlyList<string> Errors);

/// <summary>
/// Provider-neutral seam for every interaction with a Peppol Access Point. This interface —
/// and the deterministic <see cref="SandboxPeppolProvider"/> that is the only implementation
/// registered today — is the sole way this codebase ever touches "Peppol" as a network. No
/// direct connection to the real Peppol network is implemented or claimed anywhere; a real
/// Access Point adapter is out of scope until a provider and credentials are chosen.
/// </summary>
public interface IPeppolProvider
{
    /// <summary>Registration key, matches <c>PeppolSettings.ProviderKey</c> (e.g. "sandbox").</summary>
    string Key { get; }

    string DisplayName { get; }

    /// <summary>Whether this provider can register new participants (not used by the sandbox).</summary>
    bool SupportsRegistration { get; }

    Task<PeppolParticipantResult> ValidateParticipantAsync(string scheme, string id, CancellationToken cancellationToken);

    Task<PeppolSubmissionResult> SendDocumentAsync(PeppolOutboundRequest request, CancellationToken cancellationToken);

    Task<PeppolTransmissionStatusResult> GetTransmissionStatusAsync(string providerMessageId, CancellationToken cancellationToken);

    Task<PeppolDocumentValidationResult> ValidateDocumentAsync(string xml, CancellationToken cancellationToken);
}
