using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Notifications.Entities;

namespace TransportationService.Api.Modules.Notifications.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).IsRequired().HasMaxLength(50);
        builder.Property(n => n.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Title).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(1000);
        builder.Property(n => n.LinkPath).HasMaxLength(300);
        builder.Property(n => n.DedupeKey).HasMaxLength(200);

        builder.HasIndex(n => new { n.TenantId, n.UserId, n.IsRead });
        builder.HasIndex(n => new { n.TenantId, n.UserId, n.IsArchived });
        // Non-unique: fan-out writes one row per recipient under the same key; dedupe filters
        // on unresolved rows in the service.
        builder.HasIndex(n => new { n.TenantId, n.DedupeKey });
        builder.HasIndex(n => new { n.TenantId, n.ExpiresAt });

        builder.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(n => !n.IsDeleted);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => new { p.UserId, p.Category })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
