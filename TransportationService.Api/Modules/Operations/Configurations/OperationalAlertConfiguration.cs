using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Operations.Entities;

namespace TransportationService.Api.Modules.Operations.Configurations;

public class OperationalAlertConfiguration : IEntityTypeConfiguration<OperationalAlert>
{
    public void Configure(EntityTypeBuilder<OperationalAlert> builder)
    {
        builder.ToTable("operational_alerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Category).IsRequired().HasMaxLength(40);
        builder.Property(a => a.Source).IsRequired().HasMaxLength(60);
        builder.Property(a => a.Title).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Message).IsRequired().HasMaxLength(1000);
        builder.Property(a => a.LinkPath).HasMaxLength(300);
        builder.Property(a => a.RelatedEntityType).HasMaxLength(40);
        builder.Property(a => a.DedupeKey).IsRequired().HasMaxLength(120);

        // One live alert per condition per tenant (soft-deleted rows excluded).
        builder.HasIndex(a => new { a.TenantId, a.DedupeKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(a => new { a.TenantId, a.Status, a.Severity });

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
