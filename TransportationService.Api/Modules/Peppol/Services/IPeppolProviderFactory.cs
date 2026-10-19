using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>Resolves the configured <see cref="IPeppolProvider"/> by its <c>ProviderKey</c>.</summary>
public interface IPeppolProviderFactory
{
    IPeppolProvider Resolve(string providerKey);

    /// <summary>B4: the key resolves to a provider that really transmits to the LIVE network.</summary>
    bool IsLiveCapable(string? providerKey);

    /// <summary>B4: at least one live-capable provider is registered (none today).</summary>
    bool HasLiveProvider { get; }

    /// <summary>Keys of every registered provider (health/diagnostics).</summary>
    IReadOnlyCollection<string> ProviderKeys { get; }
}

/// <summary>Only "sandbox" is registered today; any other key is a clear configuration error at use time.</summary>
public sealed class PeppolProviderFactory : IPeppolProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPeppolProvider> _providers;

    public PeppolProviderFactory(IEnumerable<IPeppolProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IPeppolProvider Resolve(string providerKey)
    {
        if (!string.IsNullOrWhiteSpace(providerKey) && _providers.TryGetValue(providerKey, out var provider))
        {
            return provider;
        }

        throw new DomainValidationException("providerKey",
            $"Onbekende Peppol-provider '{providerKey}'. Er is momenteel alleen een sandbox-provider beschikbaar.");
    }

    public bool IsLiveCapable(string? providerKey) =>
        !string.IsNullOrWhiteSpace(providerKey)
        && _providers.TryGetValue(providerKey, out var provider)
        && provider.SupportsLiveNetwork;

    public bool HasLiveProvider => _providers.Values.Any(p => p.SupportsLiveNetwork);

    public IReadOnlyCollection<string> ProviderKeys => _providers.Keys.ToList();
}

/// <summary>
/// B4 — Peppol "Live" fails closed. Only <see cref="SandboxPeppolProvider"/> exists today, and it
/// FAKES delivery (Delivered on the third poll). Belgian B2B e-invoicing is legally binding, so a
/// Live configuration must never run through it: Live can only be configured, queued, dispatched,
/// polled or updated by webhook when the provider claims <see cref="IPeppolProvider.SupportsLiveNetwork"/>.
/// Without one, every path answers with <see cref="NotConfiguredMessage"/>; Sandbox stays fully usable.
/// </summary>
public static class PeppolLivePolicy
{
    /// <summary>Short UI/API label (also the frontend copy).</summary>
    public const string NotConfiguredLabel = "Peppol Live-provider niet geconfigureerd";

    public const string NotConfiguredMessage =
        NotConfiguredLabel + ": er is geen echte (gecertificeerde) Access-Point-provider geregistreerd, "
        + "dus Live-verzending is uitgeschakeld en er wordt niets naar het Peppol-netwerk verzonden. "
        + "Gebruik Sandbox om te testen.";

    /// <summary>Validation-checklist issue code for an invoice whose legal entity is set to Live.</summary>
    public const string IssueCode = "company.peppol_live_unavailable";

    /// <summary>Null factory = unknown = NOT live-capable (fail closed).</summary>
    public static bool CanUseLive(IPeppolProviderFactory? factory, string? providerKey) =>
        factory?.IsLiveCapable(providerKey) == true;

    /// <summary>True when this environment/provider combination may NOT transmit.</summary>
    public static bool IsBlocked(IPeppolProviderFactory? factory, Entities.PeppolEnvironment environment, string? providerKey) =>
        environment == Entities.PeppolEnvironment.Live && !CanUseLive(factory, providerKey);
}
