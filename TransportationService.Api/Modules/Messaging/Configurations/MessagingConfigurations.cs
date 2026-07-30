using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Messaging.Entities;

namespace TransportationService.Api.Modules.Messaging.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.Kind).IsRequired().HasMaxLength(50);
        builder.Property(m => m.OwnerType).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.RecipientAddress).IsRequired().HasMaxLength(320);
        builder.Property(m => m.RecipientName).HasMaxLength(200);
        builder.Property(m => m.Language).HasMaxLength(5);
        builder.Property(m => m.Subject).HasMaxLength(300);
        builder.Property(m => m.Body).HasMaxLength(8000);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.FailureReason).HasMaxLength(1000);
        builder.Property(m => m.IdempotencyKey).IsRequired().HasMaxLength(300);
        builder.Property(m => m.RelatedEntityType).HasMaxLength(100);
        builder.Property(m => m.RelatedEntityId).HasMaxLength(100);

        builder.HasIndex(m => new { m.TenantId, m.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
        builder.HasIndex(m => new { m.TenantId, m.CreatedAt });

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class MessagingProfileConfiguration : IEntityTypeConfiguration<MessagingProfile>
{
    public void Configure(EntityTypeBuilder<MessagingProfile> builder)
    {
        builder.ToTable("messaging_profiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.OwnerType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.EmailAddress).HasMaxLength(320);
        builder.Property(p => p.PhoneNumber).HasMaxLength(30);
        builder.Property(p => p.ExtraRecipientsJson).HasMaxLength(2000);
        builder.Property(p => p.EnabledKindsJson).HasMaxLength(2000);
        builder.Property(p => p.PreferredLanguage).HasMaxLength(5);
        builder.Property(p => p.FallbackChannel).HasConversion<string>().HasMaxLength(10);

        builder.HasIndex(p => new { p.TenantId, p.OwnerType, p.OwnerId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Kind).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Language).HasMaxLength(5);
        builder.Property(t => t.Subject).HasMaxLength(300);
        builder.Property(t => t.Body).IsRequired().HasMaxLength(8000);
        builder.Property(t => t.BodyHtml).HasMaxLength(20000);

        // Two filtered unique indexes rather than one: Postgres treats every NULL as distinct,
        // so a single index on (TenantId, CustomerId, Kind, Channel, Language) would silently
        // allow duplicate tenant-default (CustomerId IS NULL) rows.
        builder.HasIndex(t => new { t.TenantId, t.Kind, t.Channel, t.Language })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"CustomerId\" IS NULL");
        builder.HasIndex(t => new { t.TenantId, t.CustomerId, t.Kind, t.Channel, t.Language })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"CustomerId\" IS NOT NULL");

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.ToTable("notification_rules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.EventKey).IsRequired().HasMaxLength(80);
        builder.Property(r => r.RecipientsJson).HasMaxLength(4000);

        builder.HasIndex(r => new { r.TenantId, r.EventKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class CustomerNotificationOverrideConfiguration : IEntityTypeConfiguration<CustomerNotificationOverride>
{
    public void Configure(EntityTypeBuilder<CustomerNotificationOverride> builder)
    {
        builder.ToTable("customer_notification_overrides");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.EventKey).IsRequired().HasMaxLength(80);

        builder.HasIndex(o => new { o.TenantId, o.CustomerId, o.EventKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}
