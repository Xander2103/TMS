using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Peppol.Entities;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>One Live settings row that cannot transmit (no live-capable provider).</summary>
public sealed record PeppolLiveBlockedSetting(Guid TenantId, Guid LegalEntityId, string ProviderKey, bool Enabled);

/// <summary>Honest Peppol state for the startup guard and the "peppol" health check.</summary>
public sealed record PeppolHealthSnapshot(
    bool LiveProviderConfigured,
    IReadOnlyCollection<string> ProviderKeys,
    IReadOnlyList<PeppolLiveBlockedSetting> LiveSettingsWithoutProvider,
    int InterruptedSubmissions,
    int StaleInFlight,
    int LiveDeliveredWithoutLiveProvider);

/// <summary>
/// B4 state, read-only. Peppol is OPTIONAL: nothing here stops the application — a Live
/// configuration without a live-capable provider is reported (critical log at startup, Unhealthy
/// "peppol" health check) while the runtime guards (<see cref="PeppolLivePolicy"/>) keep sending
/// disabled for exactly those legal entities. Sandbox keeps working.
/// </summary>
public static class PeppolLiveGuard
{
    /// <summary>In-flight (Submitted/Accepted) longer than this without a final status = stale (EXT-03).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    public static async Task<PeppolHealthSnapshot> SnapshotAsync(
        TransportationDbContext db, IPeppolProviderFactory factory, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var liveSettings = await db.PeppolSettings.AsNoTracking()
            .Where(s => s.Environment == PeppolEnvironment.Live)
            .Select(s => new PeppolLiveBlockedSetting(s.TenantId, s.LegalEntityId, s.ProviderKey, s.Enabled))
            .ToListAsync(cancellationToken);
        var blocked = liveSettings.Where(s => !factory.IsLiveCapable(s.ProviderKey)).ToList();

        var reconcileCutoff = nowUtc - PeppolDispatcher.SubmittingReconcileAfter;
        var interrupted = await db.PeppolTransmissions.AsNoTracking().CountAsync(
            t => t.Status == PeppolTransmissionStatus.Submitting
                 && (t.SubmissionClaimedAt == null || t.SubmissionClaimedAt <= reconcileCutoff), cancellationToken);

        var staleCutoff = nowUtc - StaleAfter;
        var stale = await db.PeppolTransmissions.AsNoTracking().CountAsync(
            t => (t.Status == PeppolTransmissionStatus.SubmittedToProvider
                  || t.Status == PeppolTransmissionStatus.AcceptedByProvider)
                 && t.CreatedAt <= staleCutoff, cancellationToken);

        // Historic damage from before the fail-closed policy: Live rows "delivered" by a provider
        // that cannot reach the live network (the sandbox). Reported, never rewritten.
        var liveDelivered = await db.PeppolTransmissions.AsNoTracking()
            .Where(t => t.Environment == PeppolEnvironment.Live && t.Status == PeppolTransmissionStatus.Delivered)
            .Select(t => t.ProviderKey)
            .ToListAsync(cancellationToken);
        var liveDeliveredWithoutLive = liveDelivered.Count(key => !factory.IsLiveCapable(key));

        return new PeppolHealthSnapshot(
            factory.HasLiveProvider, factory.ProviderKeys, blocked, interrupted, stale, liveDeliveredWithoutLive);
    }

    /// <summary>
    /// Startup (every environment): logs a CRITICAL error per Live settings row that cannot
    /// transmit, and per historic Live "delivery" by a non-live provider. Never throws for the
    /// Peppol state itself — Peppol is optional and must not take the whole application down.
    /// </summary>
    public static async Task CheckAtStartupAsync(
        TransportationDbContext db, IPeppolProviderFactory factory, TimeProvider timeProvider, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        PeppolHealthSnapshot snapshot;
        try
        {
            snapshot = await SnapshotAsync(db, factory, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Peppol startup check failed; Live stays refused by the runtime guards.");
            return;
        }

        foreach (var row in snapshot.LiveSettingsWithoutProvider)
        {
            logger.LogCritical(
                "Peppol is set to LIVE for tenant {TenantId}, legal entity {LegalEntityId} (provider '{ProviderKey}', enabled: {Enabled}), "
                + "but no live-capable Peppol provider is registered. Peppol sending stays DISABLED for this legal entity "
                + "(nothing is submitted, polled or marked delivered). Switch it to Sandbox or configure a certified Access Point.",
                row.TenantId, row.LegalEntityId, row.ProviderKey, row.Enabled);
        }

        if (snapshot.LiveDeliveredWithoutLiveProvider > 0)
        {
            logger.LogCritical(
                "{Count} Peppol transmission(s) are marked Delivered in the LIVE environment by a provider that cannot reach the "
                + "live network (sandbox simulation from before the fail-closed policy). Those invoices were NOT delivered via Peppol; "
                + "verify and resend them through another channel.", snapshot.LiveDeliveredWithoutLiveProvider);
        }
    }
}

/// <summary>
/// "peppol"-tagged health check (deliberately NOT "ready": Peppol is optional, its state must
/// never take the API out of a load balancer). Unhealthy = Live configured that cannot transmit;
/// Degraded = interrupted submissions awaiting reconciliation, week-old in-flight transmissions
/// or historic Live "deliveries" by the sandbox; Healthy otherwise (data says which providers exist).
/// </summary>
public sealed class PeppolHealthCheck : IHealthCheck
{
    public const string Name = "peppol";
    public const string Tag = "peppol";

    private readonly TransportationDbContext _db;
    private readonly IPeppolProviderFactory _factory;
    private readonly TimeProvider _timeProvider;

    public PeppolHealthCheck(TransportationDbContext db, IPeppolProviderFactory factory, TimeProvider timeProvider)
    {
        _db = db;
        _factory = factory;
        _timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await PeppolLiveGuard.SnapshotAsync(_db, _factory, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["liveProviderConfigured"] = snapshot.LiveProviderConfigured,
            ["providers"] = string.Join(",", snapshot.ProviderKeys.OrderBy(k => k, StringComparer.Ordinal)),
            ["liveSettingsWithoutProvider"] = snapshot.LiveSettingsWithoutProvider.Count,
            ["interruptedSubmissions"] = snapshot.InterruptedSubmissions,
            ["staleInFlight"] = snapshot.StaleInFlight,
            ["liveDeliveredWithoutLiveProvider"] = snapshot.LiveDeliveredWithoutLiveProvider,
        };

        if (snapshot.LiveSettingsWithoutProvider.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"{PeppolLivePolicy.NotConfiguredLabel}: {snapshot.LiveSettingsWithoutProvider.Count} juridische entiteit(en) "
                + "staan op Live; Peppol-verzending is voor hen uitgeschakeld.", data: data);
        }

        if (snapshot.InterruptedSubmissions > 0 || snapshot.StaleInFlight > 0 || snapshot.LiveDeliveredWithoutLiveProvider > 0)
        {
            return HealthCheckResult.Degraded("Peppol heeft verzendingen die opvolging vragen.", data: data);
        }

        return HealthCheckResult.Healthy(
            snapshot.LiveProviderConfigured
                ? "Peppol: Live-provider geconfigureerd."
                : "Peppol: alleen Sandbox beschikbaar (Live-verzending uitgeschakeld).",
            data);
    }
}
