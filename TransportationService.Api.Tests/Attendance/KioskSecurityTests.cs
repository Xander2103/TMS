using Microsoft.Extensions.Configuration;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Security;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Kiosk-aanvalsoppervlak: device-authenticatie (secret-hash, disabled/rotatie),
/// generieke "Code ongeldig."-respons zonder enumeratieverschillen, credential-lockout
/// met backoff, single-use interactietokens gebonden aan het device, tenant-isolatie
/// tussen prikklok en PIN, kiosk-uitschakeling en fail-closed gedrag zonder pepper.
/// </summary>
public class KioskSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
    private static readonly string Pepper = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, Guid DeviceId, string DeviceKey,
        TestClock Clock, AttendancePinHasher PinHasher, KioskInteractionTokenStore Tokens)
    {
        public KioskPunchService Sut() => new(Db.Context, PinHasher, Tokens, Clock);

        public AttendanceCredentialService Credentials(Guid? tenantOverride = null)
        {
            var tenant = new DevTenantContext(tenantOverride ?? TenantId);
            return new AttendanceCredentialService(Db.Context, tenant, PinHasher,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)));
        }

        public KioskDeviceService Devices()
        {
            var tenant = new DevTenantContext(TenantId);
            return new KioskDeviceService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)));
        }
    }

    private static AttendancePinHasher Hasher(string? pepper = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Attendance:PinPepper"] = pepper ?? Pepper })
            .Build();
        return new AttendancePinHasher(new PasswordHasher(), config);
    }

    private static async Task<Harness> SeedAsync(string? pepper = null)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var secret = KioskDeviceSecrets.GenerateSecret();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Locations.Add(new Location { Id = locationId, TenantId = tenantId, Code = "MAG", Name = "Magazijn", IsActive = true });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "E-001",
            FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        db.Context.KioskDevices.Add(new KioskDevice
        {
            Id = deviceId, TenantId = tenantId, Name = "Prikklok magazijn", LocationId = locationId,
            IsActive = true, SecretHash = KioskDeviceSecrets.Hash(secret),
        });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        return new Harness(db, tenantId, employeeId, deviceId,
            KioskDeviceSecrets.BuildDeviceKey(deviceId, secret), clock, Hasher(pepper), new KioskInteractionTokenStore(clock));
    }

    private static async Task<string> SetPinAsync(Harness h, string pin = "1234")
    {
        var result = await h.Credentials().SetPinAsync(h.EmployeeId, pin, CancellationToken.None);
        Assert.Equal(AttendanceCredentialOutcome.Success, result.Outcome);
        return pin;
    }

    [Fact]
    public async Task Identify_WithValidDeviceAndPin_ReturnsMinimalStateAndToken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);

        var result = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);

        Assert.Equal(KioskOutcome.Success, result.Outcome);
        Assert.Equal("Jan", result.FirstName);
        Assert.NotNull(result.InteractionToken);
        Assert.Equal(AttendanceLiveStatus.NotClockedIn, result.Status!.Status);
    }

    [Fact]
    public async Task Identify_WrongAndUnknownPin_YieldIdenticalGenericResponse()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);
        var sut = h.Sut();

        var wrong = await sut.IdentifyAsync(h.DeviceKey, "9999", CancellationToken.None);
        var unknown = await sut.IdentifyAsync(h.DeviceKey, "555555", CancellationToken.None);

        Assert.Equal(KioskOutcome.InvalidCode, wrong.Outcome);
        Assert.Equal(KioskOutcome.InvalidCode, unknown.Outcome);
        Assert.Equal(wrong.Error, unknown.Error);
        Assert.Null(wrong.FirstName);
        Assert.Null(wrong.InteractionToken);
    }

    [Fact]
    public async Task Identify_FailsGenerically_ForDisabledCredential_AndInactiveEmployee()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);

        await h.Credentials().DisableAsync(h.EmployeeId, CancellationToken.None);
        var disabled = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.InvalidCode, disabled.Outcome);

        await h.Credentials().SetPinAsync(h.EmployeeId, "1234", CancellationToken.None);
        var employee = h.Db.Context.Employees.Single(e => e.Id == h.EmployeeId);
        employee.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var inactive = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.InvalidCode, inactive.Outcome);
    }

    [Fact]
    public async Task Identify_UnknownOrDisabledDevice_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);
        var sut = h.Sut();

        Assert.Equal(KioskOutcome.InvalidDevice, (await sut.IdentifyAsync(null, "1234", CancellationToken.None)).Outcome);
        Assert.Equal(KioskOutcome.InvalidDevice, (await sut.IdentifyAsync("garbage", "1234", CancellationToken.None)).Outcome);
        Assert.Equal(KioskOutcome.InvalidDevice,
            (await sut.IdentifyAsync(KioskDeviceSecrets.BuildDeviceKey(h.DeviceId, "verkeerd-secret"), "1234", CancellationToken.None)).Outcome);

        var device = h.Db.Context.KioskDevices.Single(d => d.Id == h.DeviceId);
        device.IsActive = false;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(KioskOutcome.InvalidDevice, (await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Identify_KioskDisabledInSettings_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);
        h.Db.Context.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, KioskEnabled = false,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);

        Assert.Equal(KioskOutcome.KioskDisabled, result.Outcome);
    }

    [Fact]
    public async Task Lockout_AfterFiveFailures_BlocksEvenTheCorrectPin_UntilBackoffExpires()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);
        var sut = h.Sut();

        for (var i = 0; i < KioskPunchService.LockoutThreshold; i++)
        {
            await sut.IdentifyAsync(h.DeviceKey, "0000", CancellationToken.None);
        }

        var lockedOut = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.InvalidCode, lockedOut.Outcome);

        h.Clock.Advance(TimeSpan.FromMinutes(6));
        var afterBackoff = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.Success, afterBackoff.Outcome);

        var credential = h.Db.Context.AttendanceCredentials.Single();
        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.Null(credential.LockedUntil);
        var device = h.Db.Context.KioskDevices.Single(d => d.Id == h.DeviceId);
        Assert.Equal(0, device.FailedAttemptCount);
        Assert.Null(device.LockedUntil);
    }

    [Fact]
    public async Task InteractionToken_IsSingleUse_DeviceBound_AndExpires()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);
        var sut = h.Sut();

        // Single use.
        var identify = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        var first = await sut.PunchAsync(h.DeviceKey, identify.InteractionToken, KioskPunchAction.ClockIn, CancellationToken.None);
        Assert.Equal(KioskOutcome.Success, first.Outcome);
        var replay = await sut.PunchAsync(h.DeviceKey, identify.InteractionToken, KioskPunchAction.ClockOut, CancellationToken.None);
        Assert.Equal(KioskOutcome.TokenExpired, replay.Outcome);

        // Devicegebonden: token van device A werkt niet op device B.
        var second = await h.Devices().CreateAsync(new SaveKioskDeviceRequest("Prikklok kantoor", null), CancellationToken.None);
        var identify2 = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        var crossDevice = await sut.PunchAsync(second!.DeviceKey, identify2.InteractionToken, KioskPunchAction.ClockOut, CancellationToken.None);
        Assert.Equal(KioskOutcome.TokenExpired, crossDevice.Outcome);

        // Verval na 45 seconden.
        var identify3 = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromSeconds(46));
        var expired = await sut.PunchAsync(h.DeviceKey, identify3.InteractionToken, KioskPunchAction.ClockOut, CancellationToken.None);
        Assert.Equal(KioskOutcome.TokenExpired, expired.Outcome);
    }

    [Fact]
    public async Task Punch_StampsKioskSourceDeviceAndLocation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);
        var sut = h.Sut();

        var identify = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        var result = await sut.PunchAsync(h.DeviceKey, identify.InteractionToken, KioskPunchAction.ClockIn, CancellationToken.None);

        Assert.Equal(KioskOutcome.Success, result.Outcome);
        var session = h.Db.Context.AttendanceSessions.Single();
        Assert.Equal(AttendanceSource.Kiosk, session.ClockInSource);
        Assert.Equal(h.DeviceId, session.KioskDeviceId);
        Assert.NotNull(session.LocationId);
        var device = h.Db.Context.KioskDevices.Single(d => d.Id == h.DeviceId);
        Assert.NotNull(device.LastPunchAt);
    }

    [Fact]
    public async Task CrossTenant_PinFromAnotherTenant_IsInvisibleToThisDevice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Zelfde PIN-cijfers bestaan alleen bij een ANDERE tenant.
        var otherTenant = Guid.NewGuid();
        var otherEmployee = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Employees.Add(new Employee
        {
            Id = otherEmployee, TenantId = otherTenant, EmployeeNumber = "X-1",
            FirstName = "Eva", LastName = "Claes", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var otherResult = await h.Credentials(otherTenant).SetPinAsync(otherEmployee, "1234", CancellationToken.None);
        Assert.Equal(AttendanceCredentialOutcome.Success, otherResult.Outcome);

        var result = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);

        Assert.Equal(KioskOutcome.InvalidCode, result.Outcome);
    }

    [Fact]
    public async Task MissingPepper_FailsClosed_ForKioskAndCredentialManagement()
    {
        var h = await SeedAsync(pepper: "");
        using var _ = h.Db;

        var identify = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.NotConfigured, identify.Outcome);

        var credential = await h.Credentials().SetPinAsync(h.EmployeeId, "1234", CancellationToken.None);
        Assert.Equal(AttendanceCredentialOutcome.NotConfigured, credential.Outcome);
    }

    [Fact]
    public async Task KioskLanguage_DeviceDefaultInPing_PersonalLanguageOnlyAfterValidIdentify()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);

        var device = h.Db.Context.KioskDevices.Single(d => d.Id == h.DeviceId);
        device.DefaultLanguage = "fr";
        var employee = h.Db.Context.Employees.Single(e => e.Id == h.EmployeeId);
        employee.PreferredLanguageCode = "en";
        await h.Db.Context.SaveChangesAsync();
        var sut = h.Sut();

        // Beginscherm: device-default, geen persoonsgebonden taal.
        var ping = await sut.PingAsync(h.DeviceKey, CancellationToken.None);
        Assert.Equal("fr", ping.DefaultLanguage);

        // Foute code lekt géén persoonlijke taal (privacy §18).
        var invalid = await sut.IdentifyAsync(h.DeviceKey, "9999", CancellationToken.None);
        Assert.Null(invalid.PreferredLanguage);

        // Geldige identificatie: persoonlijke taal beschikbaar voor het interactiescherm.
        var identify = await sut.IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.Success, identify.Outcome);
        Assert.Equal("en", identify.PreferredLanguage);
    }

    [Fact]
    public async Task RotateSecret_InvalidatesTheOldDeviceKey()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SetPinAsync(h);

        var rotated = await h.Devices().RotateSecretAsync(h.DeviceId, CancellationToken.None);
        Assert.NotNull(rotated);

        var oldKey = await h.Sut().IdentifyAsync(h.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.InvalidDevice, oldKey.Outcome);

        var newKey = await h.Sut().IdentifyAsync(rotated!.DeviceKey, "1234", CancellationToken.None);
        Assert.Equal(KioskOutcome.Success, newKey.Outcome);
    }
}
