using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Security;

/// <summary>
/// H5: a single known value encrypted with the column-encryption key the database was first
/// written with. Restoring a dump next to the WRONG key (or losing a previous key after a
/// rotation) used to go unnoticed until an HR page returned 500; the canary turns that into a
/// refused start and an unhealthy readiness probe. System-scoped, never tenant data.
/// </summary>
public class ColumnEncryptionCanary
{
    public const int SingletonId = 1;

    public int Id { get; set; }

    /// <summary>Raw stored ciphertext (<c>enc:v1:</c>…); written and read without the EF converter.</summary>
    public string Value { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ColumnEncryptionCanaryConfiguration : IEntityTypeConfiguration<ColumnEncryptionCanary>
{
    public void Configure(EntityTypeBuilder<ColumnEncryptionCanary> builder)
    {
        builder.ToTable("column_encryption_canaries");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Value).IsRequired().HasMaxLength(500);
    }
}

public sealed record ColumnEncryptionCheckResult(bool Healthy, string Description);

/// <summary>
/// Verifies that the configured key ring can decrypt what the database holds: the canary row
/// plus a small sample of real encrypted employee identifiers. Used at startup (refuse to start)
/// and by the <c>/health/ready</c> column-encryption check. Descriptions never contain key
/// material or decrypted values.
/// </summary>
public static class ColumnEncryptionVerifier
{
    /// <summary>The fixed plaintext of the canary; not secret.</summary>
    public const string CanaryPlaintext = "tms-column-encryption-canary-v1";

    private static readonly string[] SampleQueries =
    [
        "SELECT \"NationalRegisterNumber\" AS \"Value\" FROM employees WHERE \"NationalRegisterNumber\" LIKE 'enc:v1:%' LIMIT 3",
        "SELECT \"IdentityCardNumber\" AS \"Value\" FROM employees WHERE \"IdentityCardNumber\" LIKE 'enc:v1:%' LIMIT 3",
    ];

    public static async Task<ColumnEncryptionCheckResult> VerifyAsync(
        TransportationDbContext db, bool allowWrite, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!ColumnEncryption.IsConfigured)
        {
            // Pass-through mode (Development only; StartupSecurityValidator refuses it elsewhere).
            return new ColumnEncryptionCheckResult(true, "Kolomversleuteling niet geconfigureerd (pass-through).");
        }

        var canaries = db.Set<ColumnEncryptionCanary>();
        var canary = await canaries.FirstOrDefaultAsync(c => c.Id == ColumnEncryptionCanary.SingletonId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (canary is null)
        {
            if (allowWrite)
            {
                canaries.Add(new ColumnEncryptionCanary
                {
                    Id = ColumnEncryptionCanary.SingletonId,
                    Value = ColumnEncryption.Protect(CanaryPlaintext),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        else if (!ColumnEncryption.TryUnprotect(canary.Value, out var plaintext, out var usedActiveKey)
                 || plaintext != CanaryPlaintext)
        {
            return new ColumnEncryptionCheckResult(false,
                "De geconfigureerde ColumnEncryption-sleutel(s) kunnen de controlewaarde in de database niet ontsleutelen.");
        }
        else if (!usedActiveKey && allowWrite)
        {
            // Opened by a PREVIOUS key after a rotation: re-seal with the active key so the
            // canary keeps tracking the key that new data is written with.
            canary.Value = ColumnEncryption.Protect(CanaryPlaintext);
            canary.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var sql in SampleQueries)
        {
            // Raw SQL on purpose: the EF value converter would throw on the first bad value, and
            // only ciphertext rows (enc:v1:) are relevant; legacy plaintext needs no key.
            var sample = await db.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);
            if (sample.Any(value => !ColumnEncryption.TryUnprotect(value, out _, out _)))
            {
                return new ColumnEncryptionCheckResult(false,
                    "Bestaande versleutelde personeelsgegevens kunnen niet worden ontsleuteld met de geconfigureerde ColumnEncryption-sleutel(s).");
            }
        }

        return new ColumnEncryptionCheckResult(true, "Kolomversleuteling: sleutel komt overeen met de database.");
    }

    /// <summary>
    /// Startup gate: refuses to start (with an actionable message, never the key) when the
    /// configured key ring cannot decrypt data that already exists.
    /// </summary>
    public static async Task EnsureKeyMatchesDatabaseAsync(
        TransportationDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await VerifyAsync(db, allowWrite: true, clock, cancellationToken);
        if (!result.Healthy)
        {
            throw new InvalidOperationException(
                result.Description + " Refusing to start: writing new data with this key would make the database "
                + "partly unreadable. Configure the key this database was written with (ColumnEncryption:Key, and "
                + "after a rotation the old key under ColumnEncryption:PreviousKeys), e.g. from the vault entry that "
                + "belongs to this backup.");
        }
    }
}
