using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Integrations.Entities;

namespace TransportationService.Api.Modules.Integrations.Configurations;

public class CalendarSyncItemConfiguration : IEntityTypeConfiguration<CalendarSyncItem>
{
    public void Configure(EntityTypeBuilder<CalendarSyncItem> builder)
    {
        builder.ToTable("calendar_sync_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.EventType).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Title).IsRequired().HasMaxLength(300);
        builder.Property(i => i.Operation).HasConversion<string>().HasMaxLength(10);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.ExternalEventId).HasMaxLength(200);
        builder.Property(i => i.ErrorDetail).HasMaxLength(1000);

        builder.HasIndex(i => new { i.TenantId, i.EventType, i.EntityId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(i => new { i.Status, i.NextAttemptAt });

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
