using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// Shared EF mapping for every lookup table: column limits, tenant-unique code (as a
/// filtered index so a soft-deleted code can be reused), and the soft-delete query filter.
/// Concrete configurations only supply the physical table name.
/// </summary>
public abstract class LookupEntityTypeConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : LookupEntity
{
    protected abstract string TableName { get; }

    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(150);
        builder.Property(e => e.Description).HasMaxLength(1000);

        // Unique per tenant, but only among live rows so codes can be reused after a soft delete.
        builder.HasIndex(e => new { e.TenantId, e.Code })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.IsActive });

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
