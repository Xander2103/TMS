using TransportationService.Api.Modules.Messaging.Services;

namespace TransportationService.Api.Modules.Security;

/// <summary>
/// Fail-fast guard against production-unsafe configuration, run once after the host is built and
/// before it starts serving. A single mis-set environment flag must never be enough to turn a
/// production deployment into an insecure one, so the dangerous Development-only affordances are
/// re-checked here at the host level (defence in depth on top of the per-option validators).
/// </summary>
public static class StartupSecurityValidator
{
    public static void Validate(IHostEnvironment environment, IConfiguration configuration, IServiceProvider services)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        // Dev impersonation headers (X-Dev-User-Id / X-Dev-Tenant-Id) are a full authentication
        // bypass; they must never be enabled outside Development.
        if (configuration.GetValue<bool>(TransportationService.Api.Modules.Tenancy.TenantContextMiddleware.ImpersonationFlag))
        {
            throw new InvalidOperationException(
                "Dev:AllowImpersonationHeaders is enabled outside Development. This would allow header-based "
                + "authentication bypass. Refusing to start.");
        }

        // The Development file sink (and the unconfigured placeholder) persist / expose raw
        // security tokens. A non-Development host must have a real e-mail provider or it fails
        // closed at startup rather than leaking tokens or silently dropping mail at runtime.
        var email = services.GetService<IEmailProvider>();
        if (email is null or UnconfiguredEmailProvider or DevelopmentSinkProvider)
        {
            throw new InvalidOperationException(
                "No production e-mail provider is configured (resolved provider: "
                + (email?.GetType().Name ?? "none")
                + "). Configure a real IEmailProvider; refusing to start to avoid exposing security tokens.");
        }

        // Fase 9: special-category identifiers (NRN, identity-card number) must never reach a
        // non-Development database as plaintext — pass-through mode is a Development affordance.
        if (string.IsNullOrWhiteSpace(configuration["ColumnEncryption:Key"]))
        {
            throw new InvalidOperationException(
                "ColumnEncryption:Key is not configured. Special-category identifiers would be stored "
                + "as plaintext. Provide a 32-byte base64 key from the vault; refusing to start.");
        }

        // Prikklok-PIN-pepper: OPTIONEEL (zonder pepper is de kiosk fail-closed uitgeschakeld),
        // maar een geconfigureerde-maar-ongeldige waarde is een stille misconfiguratie en dus fataal.
        var pinPepper = configuration["Attendance:PinPepper"];
        if (!string.IsNullOrWhiteSpace(pinPepper)
            && Modules.Attendance.Security.AttendancePinHasher.DecodePepper(pinPepper) is null)
        {
            throw new InvalidOperationException(
                "Attendance:PinPepper is configured but invalid (expected base64, at least 32 bytes). "
                + "Fix or remove the value; refusing to start with a silently broken kiosk configuration.");
        }
    }
}
