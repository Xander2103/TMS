using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IKioskInteractionTokenStore
{
    string Issue(Guid tenantId, Guid employeeId, Guid deviceId);
    KioskInteraction? TryConsume(string token, Guid deviceId);
}

public sealed record KioskInteraction(Guid TenantId, Guid EmployeeId, Guid DeviceId);

/// <summary>
/// Kortlevende interactietokens voor de kioskflow (§ twee-stappenmodel): na een geldige
/// PIN krijgt de kiosk géén algemene employee-credential maar een token dat exact één
/// attendance-actie toestaat, gebonden aan hetzelfde device, en dat na 45 seconden of
/// één gebruik vervalt. In-memory (singleton): de applicatie draait single-instance;
/// bij een herstart voert de medewerker de PIN gewoon opnieuw in.
/// </summary>
public sealed class KioskInteractionTokenStore : IKioskInteractionTokenStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(45);

    private sealed record Entry(KioskInteraction Interaction, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public KioskInteractionTokenStore(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public string Issue(Guid tenantId, Guid employeeId, Guid deviceId)
    {
        Prune();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _entries[token] = new Entry(
            new KioskInteraction(tenantId, employeeId, deviceId),
            _timeProvider.GetUtcNow().Add(Lifetime));
        return token;
    }

    public KioskInteraction? TryConsume(string token, Guid deviceId)
    {
        if (string.IsNullOrEmpty(token) || !_entries.TryRemove(token, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt < _timeProvider.GetUtcNow() || entry.Interaction.DeviceId != deviceId)
        {
            return null;
        }

        return entry.Interaction;
    }

    private void Prune()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (token, entry) in _entries)
        {
            if (entry.ExpiresAt < now)
            {
                _entries.TryRemove(token, out _);
            }
        }
    }
}
