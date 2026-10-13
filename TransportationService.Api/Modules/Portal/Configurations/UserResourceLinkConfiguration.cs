using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Portal.Entities;

namespace TransportationService.Api.Modules.Portal.Configurations;

public class UserResourceLinkConfiguration : IEntityTypeConfiguration<UserResourceLink>
{
    public void Configure(EntityTypeBuilder<UserResourceLink> builder)
    {
        builder.ToTable("user_resource_links");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.EntityType).IsRequired().HasMaxLength(40);
        builder.Property(l => l.Label).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Subtitle).HasMaxLength(200);
        builder.Property(l => l.Route).IsRequired().HasMaxLength(300);

        // One link per user per kind per resource (soft-deleted rows excluded).
        builder.HasIndex(l => new { l.TenantId, l.UserId, l.Kind, l.EntityType, l.EntityId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.UserId, l.Kind, l.TouchedAt });

        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
