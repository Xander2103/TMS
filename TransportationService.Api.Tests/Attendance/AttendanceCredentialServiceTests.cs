using Microsoft.Extensions.Configuration;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Security;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// PIN-beheer: nooit plaintext of PBKDF2-loze opslag, lengtevalidatie volgens settings,
/// tenant-unieke codes, éénmalig teruggeven van gegenereerde PIN's, audit zonder
/// PIN-waarde en reset van lockoutstatus bij een nieuwe code.
/// </summary>
public class AttendanceCredentialServiceTests
{
    private static readonly string Pepper = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, Guid SecondEmployeeId)
    {
        public AttendanceCredentialService Sut()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Attendance:PinPepper"] = Pepper })
                .Build();
            var tenant = new DevTenantContext(TenantId);
            return new AttendanceCredentialService(Db.Context, tenant,
                new AttendancePinHasher(new PasswordHasher(), config),
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var secondEmployeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.AddRange(
            new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "E-1", FirstName = "Jan", LastName = "P", IsActive = true },
            new Employee { Id = secondEmployeeId, TenantId = tenantId, EmployeeNumber = "E-2", FirstName = "Sarah", LastName = "J", IsActive = true });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, employeeId, secondEmployeeId);
    }

    [Fact]
    public async Task SetPin_StoresOnlyHashes_AndAuditsWithoutThePin()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut().SetPinAsync(h.EmployeeId, "1234", CancellationToken.None);

        Assert.Equal(AttendanceCredentialOutcome.Success, result.Outcome);
        Assert.Null(result.GeneratedPin); // expliciet gezette PIN wordt nooit teruggegeven

        var credential = h.Db.Context.AttendanceCredentials.Single();
        Assert.DoesNotContain("1234", credential.SecretHash);
        Assert.StartsWith("AQAAAA", credential.SecretHash); // ASP.NET Identity v3 PBKDF2-envelope
        Assert.NotEmpty(credential.LookupHash);
        Assert.DoesNotContain("1234", credential.LookupHash);

        var audit = h.Db.Context.AuditLogs.Single(a => a.EntityType == "AttendanceCredential");
        Assert.DoesNotContain("1234", audit.NewValuesJson ?? string.Empty);
    }

    [Fact]
    public async Task SetPin_ValidatesLengthFromSettings()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.AttendanceSettings.Add(new TransportationService.Api.Modules.Attendance.Entities.AttendanceSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, PinLength = 6,
        });
        await h.Db.Context.SaveChangesAsync();
        var sut = h.Sut();

        Assert.Equal(AttendanceCredentialOutcome.InvalidPin, (await sut.SetPinAsync(h.EmployeeId, "1234", CancellationToken.None)).Outcome);
        Assert.Equal(AttendanceCredentialOutcome.InvalidPin, (await sut.SetPinAsync(h.EmployeeId, "12345a", CancellationToken.None)).Outcome);
        Assert.Equal(AttendanceCredentialOutcome.Success, (await sut.SetPinAsync(h.EmployeeId, "123456", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task SetPin_RejectsCodeAlreadyUsedByColleague()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        await sut.SetPinAsync(h.EmployeeId, "1234", CancellationToken.None);
        var duplicate = await sut.SetPinAsync(h.SecondEmployeeId, "1234", CancellationToken.None);

        Assert.Equal(AttendanceCredentialOutcome.PinInUse, duplicate.Outcome);
    }

    [Fact]
    public async Task GeneratePin_ReturnsPinExactlyOnce_WithConfiguredLength()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut().SetPinAsync(h.EmployeeId, null, CancellationToken.None);

        Assert.Equal(AttendanceCredentialOutcome.Success, result.Outcome);
        Assert.NotNull(result.GeneratedPin);
        Assert.Equal(4, result.GeneratedPin!.Length);
        Assert.All(result.GeneratedPin, c => Assert.True(char.IsAsciiDigit(c)));

        var status = await h.Sut().GetStatusAsync(h.EmployeeId, CancellationToken.None);
        Assert.True(status.HasCredential);
        Assert.True(status.IsActive);
    }

    [Fact]
    public async Task Reset_ClearsLockout_AndDisableRevokes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.SetPinAsync(h.EmployeeId, "1234", CancellationToken.None);

        var credential = h.Db.Context.AttendanceCredentials.Single();
        credential.FailedAttemptCount = 7;
        credential.LockedUntil = DateTime.UtcNow.AddMinutes(30);
        await h.Db.Context.SaveChangesAsync();

        await sut.SetPinAsync(h.EmployeeId, "4321", CancellationToken.None);
        credential = h.Db.Context.AttendanceCredentials.Single();
        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.Null(credential.LockedUntil);

        var disabled = await sut.DisableAsync(h.EmployeeId, CancellationToken.None);
        Assert.Equal(AttendanceCredentialOutcome.Success, disabled.Outcome);
        Assert.False(h.Db.Context.AttendanceCredentials.Single().IsActive);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "AttendanceCredential" && a.Action == "Disabled");
    }
}
