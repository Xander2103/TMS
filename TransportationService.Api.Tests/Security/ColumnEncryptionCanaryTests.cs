using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Security;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// H5: the column-encryption canary. A database restored next to the wrong key (or after losing a
/// previous key) refuses to start with an actionable message that never contains key material;
/// a rotation re-seals the canary with the active key.
/// </summary>
[Collection("ColumnEncryptionSerial")]
public sealed class ColumnEncryptionCanaryTests : IDisposable
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public void Dispose() => ColumnEncryption.Initialize(null);

    private static async Task<string> StoredCanaryAsync(SqliteTestDbContext db) =>
        (await db.Context.Set<ColumnEncryptionCanary>().AsNoTracking().SingleAsync()).Value;

    [Fact]
    public async Task FreshDatabase_GetsACanary_EncryptedWithTheActiveKey()
    {
        using var db = new SqliteTestDbContext();
        ColumnEncryption.Initialize(NewKey());

        await ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(db.Context, TimeProvider.System, CancellationToken.None);

        var stored = await StoredCanaryAsync(db);
        Assert.StartsWith(ColumnEncryption.Prefix, stored);
        Assert.DoesNotContain(ColumnEncryptionVerifier.CanaryPlaintext, stored);
        Assert.True((await ColumnEncryptionVerifier.VerifyAsync(db.Context, false, TimeProvider.System, CancellationToken.None)).Healthy);
    }

    [Fact]
    public async Task WrongKey_RefusesToStart_WithAnActionableMessage_WithoutTheKey()
    {
        using var db = new SqliteTestDbContext();
        ColumnEncryption.Initialize(NewKey());
        await ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(db.Context, TimeProvider.System, CancellationToken.None);

        var wrongKey = NewKey();
        ColumnEncryption.Initialize(wrongKey);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(db.Context, TimeProvider.System, CancellationToken.None));

        Assert.Contains("Refusing to start", exception.Message);
        Assert.Contains("ColumnEncryption:PreviousKeys", exception.Message);
        Assert.DoesNotContain(wrongKey, exception.Message);
    }

    [Fact]
    public async Task ExistingEncryptedEmployeeData_UnderAnotherKey_IsDetected_EvenWithoutACanary()
    {
        using var db = new SqliteTestDbContext();
        // A dump written by an installation whose key is not the one configured now (and which
        // predates the canary): only the employee identifiers are ciphertext.
        ColumnEncryption.Initialize(NewKey());
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new TransportationService.Api.Modules.Tenancy.Entities.Tenant
        {
            Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow,
        });
        db.Context.Employees.Add(new TransportationService.Api.Modules.Employees.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = "P-1", FirstName = "An", LastName = "Peeters",
            NationalRegisterNumber = "85.07.30-033.28", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();

        ColumnEncryption.Initialize(NewKey()); // the database was written with a different key
        var result = await ColumnEncryptionVerifier.VerifyAsync(db.Context, false, TimeProvider.System, CancellationToken.None);

        Assert.False(result.Healthy);
        Assert.Contains("personeelsgegevens", result.Description);
    }

    [Fact]
    public async Task Rotation_DecryptsWithThePreviousKey_AndReSealsTheCanary()
    {
        using var db = new SqliteTestDbContext();
        var oldKey = NewKey();
        var newKey = NewKey();
        ColumnEncryption.Initialize(oldKey);
        await ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(db.Context, TimeProvider.System, CancellationToken.None);
        var sealedWithOld = await StoredCanaryAsync(db);

        ColumnEncryption.Initialize(newKey, [oldKey]);
        await ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(db.Context, TimeProvider.System, CancellationToken.None);
        var resealed = await StoredCanaryAsync(db);
        Assert.NotEqual(sealedWithOld, resealed);

        // The old key may now leave the ring as far as the canary is concerned.
        ColumnEncryption.Initialize(newKey);
        Assert.True((await ColumnEncryptionVerifier.VerifyAsync(db.Context, false, TimeProvider.System, CancellationToken.None)).Healthy);
    }

    [Fact]
    public async Task PassThroughMode_IsHealthy_AndWritesNothing()
    {
        using var db = new SqliteTestDbContext();
        ColumnEncryption.Initialize(null);

        await ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(db.Context, TimeProvider.System, CancellationToken.None);

        Assert.Equal(0, await db.Context.Set<ColumnEncryptionCanary>().CountAsync());
    }
}
