using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Notifications.Entities;

namespace TransportationService.Api.Modules.Notifications.Configurations;

public class InternalMessageConfiguration : IEntityTypeConfiguration<InternalMessage>
{
    public void Configure(EntityTypeBuilder<InternalMessage> builder)
    {
        builder.ToTable("internal_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Subject).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Body).IsRequired().HasMaxLength(8000);
        builder.Property(m => m.RelatedEntityType).HasMaxLength(50);
        builder.Property(m => m.RelatedEntityId).HasMaxLength(50);
        builder.Property(m => m.Priority).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(m => new { m.TenantId, m.SenderUserId });
        builder.HasIndex(m => new { m.TenantId, m.VisibleFrom });

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class InternalMessageRecipientConfiguration : IEntityTypeConfiguration<InternalMessageRecipient>
{
    public void Configure(EntityTypeBuilder<InternalMessageRecipient> builder)
    {
        builder.ToTable("internal_message_recipients");
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => new { r.TenantId, r.UserId, r.ReadAt });
        builder.HasIndex(r => new { r.MessageId, r.UserId }).IsUnique();

        builder.HasOne<InternalMessage>().WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
