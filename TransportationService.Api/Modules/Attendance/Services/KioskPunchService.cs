using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Security;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IKioskPunchService
{
    Task<KioskPingResult> PingAsync(string? deviceKey, CancellationToken cancellationToken);
    Task<KioskIdentifyResult> IdentifyAsync(string? deviceKey, string? pin, CancellationToken cancellationToken);
    Task<KioskPunchResult> PunchAsync(string? deviceKey, string? interactionToken, KioskPunchAction action, CancellationToken cancellationToken);
}

/// <summary>
/// De volledige kioskflow, bereikbaar zonder gebruikerssessie maar nooit zonder geldig
/// device-secret. Tweestapsmodel: (1) identify met PIN levert een kortlevend, aan het
/// device gebonden interactietoken plus minimale status; (2) punch consumeert dat token
/// voor exact één actie. De kiosk krijgt dus nooit een algemene employee-credential.
///
/// Anti-enumeratie: elke identificatiefout — onbekende code, foute code, geblokkeerde
/// of ingetrokken credential, inactieve medewerker — levert exact dezelfde respons
/// ("Code ongeldig.") en een onbekende code kost via een dummy-PBKDF2-verificatie
/// dezelfde tijd als een foute code. Brute force wordt daarnaast per credential
/// (lockout met backoff) en per IP (rate-limitpolicy "kiosk") gesmoord.
///
/// De tenant volgt uit het device (anoniem verzoek heeft geen tenantcontext); alle
/// vervolgquery's zijn expliciet tenant-gescoped, dus cross-tenant PIN-gebruik is
/// onmogelijk.
/// </summary>
public class KioskPunchService : IKioskPunchService
{
    public const int LockoutThreshold = 5;
    private static readonly TimeSpan BaseLockout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(60);
    private const string InvalidCodeMessage = "Code ongeldig.";

    private static string? _dummyHash;

    private readonly TransportationDbContext _dbContext;
    private readonly IAttendancePinHasher _pinHasher;
    private readonly IKioskInteractionTokenStore _tokenStore;
    private readonly TimeProvider _timeProvider;

    public KioskPunchService(
        TransportationDbContext dbContext,
        IAttendancePinHasher pinHasher,
        IKioskInteractionTokenStore tokenStore,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _pinHasher = pinHasher;
        _tokenStore = tokenStore;
        _timeProvider = timeProvider;
    }

    public async Task<KioskPingResult> PingAsync(string? deviceKey, CancellationToken cancellationToken)
    {
        var device = await AuthenticateDeviceAsync(deviceKey, cancellationToken);
        if (device is null)
        {
            return new KioskPingResult(KioskOutcome.InvalidDevice, null, null, "Prikklok niet herkend of uitgeschakeld.");
        }

        if (!_pinHasher.IsConfigured)
        {
            return new KioskPingResult(KioskOutcome.NotConfigured, device.Name, null,
                "De prikklok is niet geconfigureerd op de server.");
        }

        if (!await KioskEnabledAsync(device.TenantId, cancellationToken))
        {
            return new KioskPingResult(KioskOutcome.KioskDisabled, device.Name, null, "De prikklok is uitgeschakeld.");
        }

        var locationName = device.LocationId is { } locationId
            ? await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == device.TenantId && l.Id == locationId)
                .Select(l => l.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new KioskPingResult(KioskOutcome.Success, device.Name, locationName, null);
    }

    public async Task<KioskIdentifyResult> IdentifyAsync(string? deviceKey, string? pin, CancellationToken cancellationToken)
    {
        var device = await AuthenticateDeviceAsync(deviceKey, cancellationToken);
        if (device is null)
        {
            return new KioskIdentifyResult(KioskOutcome.InvalidDevice, null, null, null,
                "Prikklok niet herkend of uitgeschakeld.");
        }

        if (!_pinHasher.IsConfigured)
        {
            return new KioskIdentifyResult(KioskOutcome.NotConfigured, null, null, null,
                "De prikklok is niet geconfigureerd op de server.");
        }

        if (!await KioskEnabledAsync(device.TenantId, cancellationToken))
        {
            return new KioskIdentifyResult(KioskOutcome.KioskDisabled, null, null, null, "De prikklok is uitgeschakeld.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        device.LastSeenAt = now;

        // Device-lockout: bij PIN-only-identificatie is een foute gok niet aan één
        // credential toe te schrijven, dus na te veel ongeldige codes gaat de prikklok
        // zelf in backoff — met exact dezelfde generieke respons (geen oracle).
        if (device.LockedUntil is { } deviceLockedUntil && deviceLockedUntil > now)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return InvalidCode();
        }

        if (string.IsNullOrWhiteSpace(pin))
        {
            return await FailIdentifyAsync(device, null, now, cancellationToken);
        }

        var lookupHash = _pinHasher.ComputeLookupHash(device.TenantId, pin.Trim());
        var credential = await _dbContext.AttendanceCredentials
            .FirstOrDefaultAsync(c => c.TenantId == device.TenantId && c.LookupHash == lookupHash, cancellationToken);

        if (credential is null)
        {
            // Timing-egalisatie: onbekende code kost evenveel als een foute code.
            _pinHasher.VerifyPin(DummyHash(), pin.Trim());
            return await FailIdentifyAsync(device, null, now, cancellationToken);
        }

        if (!credential.IsActive || (credential.LockedUntil is { } lockedUntil && lockedUntil > now))
        {
            _pinHasher.VerifyPin(DummyHash(), pin.Trim());
            return await FailIdentifyAsync(device, null, now, cancellationToken);
        }

        if (!_pinHasher.VerifyPin(credential.SecretHash, pin.Trim()))
        {
            return await FailIdentifyAsync(device, credential, now, cancellationToken);
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == device.TenantId && e.Id == credential.EmployeeId)
            .Select(e => new { e.FirstName, e.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null || !employee.IsActive)
        {
            return await FailIdentifyAsync(device, null, now, cancellationToken);
        }

        credential.FailedAttemptCount = 0;
        credential.LockedUntil = null;
        credential.LastUsedAt = now;
        device.FailedAttemptCount = 0;
        device.LockedUntil = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var attendance = TenantScopedAttendance(device.TenantId);
        var status = await attendance.GetStatusAsync(credential.EmployeeId, cancellationToken);
        var token = _tokenStore.Issue(device.TenantId, credential.EmployeeId, device.Id);

        return new KioskIdentifyResult(KioskOutcome.Success, employee.FirstName, status, token, null);
    }

    public async Task<KioskPunchResult> PunchAsync(
        string? deviceKey, string? interactionToken, KioskPunchAction action, CancellationToken cancellationToken)
    {
        var device = await AuthenticateDeviceAsync(deviceKey, cancellationToken);
        if (device is null)
        {
            return new KioskPunchResult(KioskOutcome.InvalidDevice, null, null, "Prikklok niet herkend of uitgeschakeld.");
        }

        var interaction = _tokenStore.TryConsume(interactionToken ?? string.Empty, device.Id);
        if (interaction is null || interaction.TenantId != device.TenantId)
        {
            return new KioskPunchResult(KioskOutcome.TokenExpired, null, null,
                "De sessie is verlopen. Voer uw code opnieuw in.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var attendance = TenantScopedAttendance(device.TenantId);
        var context = new AttendancePunchContext(AttendanceSource.Kiosk, device.Id, device.LocationId);

        var result = action switch
        {
            KioskPunchAction.ClockIn => await attendance.ClockInAsync(interaction.EmployeeId, context, cancellationToken),
            KioskPunchAction.ClockOut => await attendance.ClockOutAsync(interaction.EmployeeId, context, cancellationToken),
            KioskPunchAction.StartBreak => await attendance.StartBreakAsync(interaction.EmployeeId, context, cancellationToken),
            KioskPunchAction.EndBreak => await attendance.EndBreakAsync(interaction.EmployeeId, context, cancellationToken),
            _ => AttendancePunchResult.Fail(AttendancePunchOutcome.NotClockedIn, "Ongeldige actie."),
        };

        // Het race-pad in AttendanceService wist de ChangeTracker; haal het device dan
        // opnieuw op zodat LastSeen/LastPunch niet stil verloren gaan.
        if (_dbContext.Entry(device).State == EntityState.Detached)
        {
            device = await _dbContext.KioskDevices.FirstAsync(d => d.Id == device.Id, cancellationToken);
        }

        device.LastSeenAt = now;
        if (result.Outcome == AttendancePunchOutcome.Success)
        {
            device.LastPunchAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return result.Outcome == AttendancePunchOutcome.Success
            ? new KioskPunchResult(KioskOutcome.Success, result.Status, now, null)
            : new KioskPunchResult(KioskOutcome.PunchRejected, null, null, result.Error);
    }

    // ── Intern ───────────────────────────────────────────────────────────────────

    /// <summary>Deviceverificatie: bestaand, actief én constant-time secretmatch — anders null (één generiek faalpad).</summary>
    private async Task<KioskDevice?> AuthenticateDeviceAsync(string? deviceKey, CancellationToken cancellationToken)
    {
        if (!KioskDeviceSecrets.TryParseDeviceKey(deviceKey, out var deviceId, out var secret))
        {
            return null;
        }

        // Anoniem verzoek: geen tenantcontext, dus expliciet op Id zoeken; de tenant
        // van het device bepaalt daarna de scope van álle vervolgquery's.
        var device = await _dbContext.KioskDevices.FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null || !device.IsActive || !KioskDeviceSecrets.Verify(device.SecretHash, secret))
        {
            return null;
        }

        return device;
    }

    private AttendanceService TenantScopedAttendance(Guid tenantId) =>
        new(_dbContext, new DevTenantContext(tenantId), _timeProvider);

    private async Task<bool> KioskEnabledAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var enabled = await _dbContext.AttendanceSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (bool?)s.KioskEnabled)
            .FirstOrDefaultAsync(cancellationToken);
        return enabled ?? true;
    }

    /// <summary>Ongeldige identificatie: teller(s) bijwerken, opslaan en de ene generieke respons geven.</summary>
    private async Task<KioskIdentifyResult> FailIdentifyAsync(
        KioskDevice device, AttendanceCredential? credential, DateTime now, CancellationToken cancellationToken)
    {
        device.FailedAttemptCount += 1;
        if (device.FailedAttemptCount >= LockoutThreshold)
        {
            device.LockedUntil = now.Add(Backoff(device.FailedAttemptCount));
        }

        if (credential is not null)
        {
            credential.FailedAttemptCount += 1;
            if (credential.FailedAttemptCount >= LockoutThreshold)
            {
                credential.LockedUntil = now.Add(Backoff(credential.FailedAttemptCount));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return InvalidCode();
    }

    private static TimeSpan Backoff(int failedAttempts)
    {
        var overshoot = failedAttempts - LockoutThreshold;
        return TimeSpan.FromTicks(Math.Min(MaxLockout.Ticks, BaseLockout.Ticks * (1L << Math.Min(overshoot, 4))));
    }

    private string DummyHash() => _dummyHash ??= _pinHasher.HashPin("0000-dummy");

    private static KioskIdentifyResult InvalidCode() =>
        new(KioskOutcome.InvalidCode, null, null, null, InvalidCodeMessage);
}
