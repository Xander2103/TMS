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
    Guid CorrelationId,
    /// <summary>H3: stable per transmission and persisted BEFORE the network call. A provider MUST
    /// treat a repeated submission with the same key as the same document (return the original
    /// message id, never deliver twice) and must be able to look it up again
    /// (<see cref="IPeppolProvider.FindSubmissionAsync"/>).</summary>
    string IdempotencyKey);

/// <summary>Immediate provider acknowledgement of a submitted document.</summary>
public sealed record PeppolSubmissionResult(bool Accepted, string? ProviderMessageId, string? Error);

/// <summary>Point-in-time transmission status as reported by the provider.</summary>
public sealed record PeppolTransmissionStatusResult(string Status, string? Detail);

/// <summary>Result of looking a submission up by its idempotency key (crash recovery).</summary>
public sealed record PeppolSubmissionLookupResult(bool Found, string? ProviderMessageId);

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

    /// <summary>
    /// B4: true ONLY for a real, certified Access Point adapter that actually transmits to the live
    /// Peppol network. Fail-closed default: a provider that does not explicitly claim it can never
    /// be used for <c>Environment = Live</c> (settings, queueing, dispatching and status updates
    /// all refuse). The sandbox never claims it.
    /// </summary>
    bool SupportsLiveNetwork => false;

    Task<PeppolParticipantResult> ValidateParticipantAsync(string scheme, string id, CancellationToken cancellationToken);

    Task<PeppolSubmissionResult> SendDocumentAsync(PeppolOutboundRequest request, CancellationToken cancellationToken);

    Task<PeppolTransmissionStatusResult> GetTransmissionStatusAsync(string providerMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// H3 crash recovery: did the provider receive a submission with this idempotency key? Used for
    /// a transmission left in <c>Submitting</c> (crash/timeout between the network call and the
    /// save) — it is reconciled from this answer and NEVER blindly resubmitted. Throw when the
    /// answer is unknown; the transmission then stays Submitting and is asked about again.
    /// </summary>
    Task<PeppolSubmissionLookupResult> FindSubmissionAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<PeppolDocumentValidationResult> ValidateDocumentAsync(string xml, CancellationToken cancellationToken);
}
