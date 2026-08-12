using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.SystemAdmin.Entities;

namespace TransportationService.Api.Modules.SystemAdmin.Configurations;

public class DatabaseBackupConfiguration : IEntityTypeConfiguration<DatabaseBackup>
{
    public void Configure(EntityTypeBuilder<DatabaseBackup> builder)
    {
        builder.ToTable("database_backups");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.FileName).IsRequired().HasMaxLength(260);
        builder.Property(b => b.Source).IsRequired().HasMaxLength(20);
        builder.Property(b => b.Status).IsRequired().HasMaxLength(20);
        builder.Property(b => b.SchemaVersion).HasMaxLength(150);
        builder.Property(b => b.Note).HasMaxLength(500);
        builder.HasIndex(b => b.CreatedAtUtc);
        // System-scoped by design: no TenantId, no tenant filter — access is permission-gated.
    }
}
