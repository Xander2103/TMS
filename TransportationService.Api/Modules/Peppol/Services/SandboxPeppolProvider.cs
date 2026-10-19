using System.Collections.Concurrent;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>
/// Deterministic in-memory sandbox Access Point adapter — the ONLY <see cref="IPeppolProvider"/>
/// implementation in this codebase (registered as a singleton; dev/test use). Behaviour is
/// fixed so tests and manual QA stay repeatable:
/// - a participant id ending in "999" is reported not found;
/// - a document/payload containing the literal marker "SANDBOX-FAIL" is refused/invalid;
/// - transmission status advances SubmittedToProvider → AcceptedByProvider → Delivered on
///   successive polls of the same provider message id, tracked by a per-instance counter
///   (reset with <see cref="ResetStatusCounters"/> between test scenarios);
/// - submissions are idempotent per idempotency key (a repeat returns the original message id)
///   and can be looked up by that key, like a real Access Point must (H3).
/// It NEVER transmits anything and therefore never claims <see cref="IPeppolProvider.SupportsLiveNetwork"/>:
/// "Delivered" from the sandbox is a simulation and is only ever accepted for Sandbox transmissions (B4).
/// </summary>
public sealed class SandboxPeppolProvider : IPeppolProvider
{
    public const string ProviderKey = "sandbox";
    private const string FailureMarker = "SANDBOX-FAIL";

    private readonly ConcurrentDictionary<string, int> _pollCounts = new();
    private readonly ConcurrentDictionary<string, string> _messageIdsByKey = new(StringComparer.Ordinal);

    public string Key => ProviderKey;
    public string DisplayName => "Sandbox (test-omgeving)";
    public bool SupportsRegistration => false;

    /// <summary>Explicit: the sandbox simulates delivery and must never serve Live (B4).</summary>
    public bool SupportsLiveNetwork => false;

    public Task<PeppolParticipantResult> ValidateParticipantAsync(string scheme, string id, CancellationToken cancellationToken)
    {
        if (id.EndsWith("999", StringComparison.Ordinal))
        {
            return Task.FromResult(new PeppolParticipantResult(false, [], null));
        }

        return Task.FromResult(new PeppolParticipantResult(
            true, ["Invoice", "CreditNote"], $"sandbox-{scheme}:{id}"));
    }

    public Task<PeppolSubmissionResult> SendDocumentAsync(PeppolOutboundRequest request, CancellationToken cancellationToken)
    {
        if (request.DocumentXml.Contains(FailureMarker, StringComparison.Ordinal))
        {
            return Task.FromResult(new PeppolSubmissionResult(
                false, null, "Sandbox: verzending geweigerd (testmarkering SANDBOX-FAIL in het document)."));
        }

        // Idempotent per key: a repeated submission is the same document (no new message, no reset).
        var messageId = _messageIdsByKey.GetOrAdd(request.IdempotencyKey, _ =>
        {
            var created = $"sbx-{request.CorrelationId:N}";
            _pollCounts[created] = 0;
            return created;
        });
        return Task.FromResult(new PeppolSubmissionResult(true, messageId, null));
    }

    public Task<PeppolSubmissionLookupResult> FindSubmissionAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_messageIdsByKey.TryGetValue(idempotencyKey, out var messageId)
            ? new PeppolSubmissionLookupResult(true, messageId)
            : new PeppolSubmissionLookupResult(false, null));

    public Task<PeppolTransmissionStatusResult> GetTransmissionStatusAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        var count = _pollCounts.AddOrUpdate(providerMessageId, 1, (_, existing) => existing + 1);
        var status = count switch
        {
            <= 1 => "SubmittedToProvider",
            2 => "AcceptedByProvider",
            _ => "Delivered",
        };
        return Task.FromResult(new PeppolTransmissionStatusResult(status, $"Sandbox poll #{count}"));
    }

    public Task<PeppolDocumentValidationResult> ValidateDocumentAsync(string xml, CancellationToken cancellationToken)
    {
        if (xml.Contains(FailureMarker, StringComparison.Ordinal))
        {
            return Task.FromResult(new PeppolDocumentValidationResult(
                false, ["Sandbox: documentmarkering SANDBOX-FAIL aangetroffen."]));
        }

        return Task.FromResult(new PeppolDocumentValidationResult(true, []));
    }

    /// <summary>Clears in-memory poll counters. Used between test scenarios / dev restarts.</summary>
    public void ResetStatusCounters()
    {
        _pollCounts.Clear();
        _messageIdsByKey.Clear();
    }
}
