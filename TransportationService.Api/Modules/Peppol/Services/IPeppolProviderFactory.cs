using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>Resolves the configured <see cref="IPeppolProvider"/> by its <c>ProviderKey</c>.</summary>
public interface IPeppolProviderFactory
{
    IPeppolProvider Resolve(string providerKey);
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
}
