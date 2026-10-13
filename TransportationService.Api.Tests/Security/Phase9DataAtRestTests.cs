using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Security;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 9: application-level column encryption for special-category identifiers. The database
/// only ever sees ciphertext once a key is configured; legacy plaintext keeps reading; rotation
/// decrypts with previous keys; tampering is detected. Tests share process-global key-ring state,
/// so they run in one (non-parallel) collection and always restore pass-through mode.
/// </summary>
[Collection("ColumnEncryptionSerial")]
public class Phase9DataAtRestTests : IDisposable
{
    private static string NewKey() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public void Dispose()
    {
        // Back to Development pass-through so the rest of the suite is unaffected.
        ColumnEncryption.Initialize(null);
    }

    [Fact]
    public void Roundtrip_EncryptsAndDecrypts()
    {
        ColumnEncryption.Initialize(NewKey());

        var stored = ColumnEncryption.Protect("86.01.01-123.45");
        Assert.StartsWith(ColumnEncryption.Prefix, stored);
        Assert.DoesNotContain("86.01.01", stored);
        Assert.Equal("86.01.01-123.45", ColumnEncryption.Unprotect(stored));
    }

    [Fact]
    public void LegacyPlaintext_ReadsThroughUnchanged()
    {
        ColumnEncryption.Initialize(NewKey());
        Assert.Equal("86.01.01-123.45", ColumnEncryption.Unprotect("86.01.01-123.45"));
    }

    [Fact]
    public void WithoutAKey_ProtectIsPassThrough()
    {
        ColumnEncryption.Initialize(null);
        Assert.Equal("86.01.01-123.45", ColumnEncryption.Protect("86.01.01-123.45"));
        Assert.False(ColumnEncryption.IsConfigured);
    }

    [Fact]
    public void KeyRotation_OldCiphertextStillDecrypts_NewWritesUseTheNewKey()
    {
        var oldKey = NewKey();
        var newKey = NewKey();

        ColumnEncryption.Initialize(oldKey);
        var storedWithOldKey = ColumnEncryption.Protect("BE-123456");

        ColumnEncryption.Initialize(newKey, [oldKey]);
        Assert.Equal("BE-123456", ColumnEncryption.Unprotect(storedWithOldKey));

        var storedWithNewKey = ColumnEncryption.Protect("BE-123456");
        ColumnEncryption.Initialize(newKey); // ring without the old key
        Assert.Equal("BE-123456", ColumnEncryption.Unprotect(storedWithNewKey));
        Assert.Throws<InvalidOperationException>(() => ColumnEncryption.Unprotect(storedWithOldKey));
    }

    [Fact]
    public void TamperedCiphertext_IsRejected()
    {
        ColumnEncryption.Initialize(NewKey());
        var stored = ColumnEncryption.Protect("86.01.01-123.45");

        var payload = Convert.FromBase64String(stored[ColumnEncryption.Prefix.Length..]);
        payload[^1] ^= 0xFF;
        var tampered = ColumnEncryption.Prefix + Convert.ToBase64String(payload);

        Assert.Throws<InvalidOperationException>(() => ColumnEncryption.Unprotect(tampered));
    }

    [Fact]
    public void InvalidKeys_AreRefusedAtInitialisation()
    {
        Assert.Throws<InvalidOperationException>(() => ColumnEncryption.Initialize("geen-base64!"));
        Assert.Throws<InvalidOperationException>(
            () => ColumnEncryption.Initialize(Convert.ToBase64String(new byte[16])));
    }

    [Fact]
    public async Task Database_OnlyEverSeesCiphertext_WhileTheApplicationReadsPlaintext()
    {
        ColumnEncryption.Initialize(NewKey());

        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "P-9",
            FirstName = "Jan", LastName = "Jansen",
            NationalRegisterNumber = "86.01.01-123.45", IdentityCardNumber = "591-1234567-89",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();

        // Application-side: transparent decryption through the value converter.
        var employee = await db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
        Assert.Equal("86.01.01-123.45", employee.NationalRegisterNumber);
        Assert.Equal("591-1234567-89", employee.IdentityCardNumber);

        // Database-side: raw column content is ciphertext.
        var connection = db.Context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"NationalRegisterNumber\", \"IdentityCardNumber\" FROM employees";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.StartsWith(ColumnEncryption.Prefix, reader.GetString(0));
        Assert.DoesNotContain("86.01.01", reader.GetString(0));
        Assert.StartsWith(ColumnEncryption.Prefix, reader.GetString(1));
    }
}

[CollectionDefinition("ColumnEncryptionSerial", DisableParallelization = true)]
public sealed class ColumnEncryptionSerialCollection;
