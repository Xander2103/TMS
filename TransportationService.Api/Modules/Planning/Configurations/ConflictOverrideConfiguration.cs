using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Configurations;

public class ConflictOverrideConfiguration : IEntityTypeConfiguration<ConflictOverride>
{
    public void Configure(EntityTypeBuilder<ConflictOverride> builder)
    {
        builder.ToTable("conflict_overrides");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.EntityType).IsRequired().HasMaxLength(40);
        builder.Property(o => o.ConflictCodes).IsRequired().HasMaxLength(1000);
        builder.Property(o => o.Reason).IsRequired().HasMaxLength(2000);

        builder.HasIndex(o => new { o.TenantId, o.EntityType, o.EntityId });

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}
