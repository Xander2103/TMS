using TransportationService.Api.Modules.Messaging.Entities;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Fail-closed placeholder e-mail provider registered outside Development while no real provider
/// exists. It never silently drops mail and — crucially — is NOT the <see cref="DevelopmentSinkProvider"/>,
/// so token-bearing flows keep their raw secrets server-side. <see cref="StartupSecurityValidator"/>
/// refuses to boot a non-Development host that still resolves this provider, turning "no mail
/// provider configured" into a startup failure rather than a runtime data leak.
/// </summary>
public sealed class UnconfiguredEmailProvider : IEmailProvider
{
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No production e-mail provider is configured. Refusing to send. Configure a real IEmailProvider.");
}

/// <summary>Fail-closed placeholder SMS provider — see <see cref="UnconfiguredEmailProvider"/>.</summary>
public sealed class UnconfiguredSmsProvider : ISmsProvider
{
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No production SMS provider is configured. Refusing to send. Configure a real ISmsProvider.");
}
