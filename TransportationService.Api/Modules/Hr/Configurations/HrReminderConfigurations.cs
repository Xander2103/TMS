using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Configurations;

public class HrReminderSettingsConfiguration : IEntityTypeConfiguration<HrReminderSettings>
{
    public void Configure(EntityTypeBuilder<HrReminderSettings> builder)
    {
        builder.ToTable("hr_reminder_settings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.BirthdayRecipientRoleCodes).IsRequired().HasMaxLength(200);
        builder.Property(s => s.SeniorityMilestoneYears).IsRequired().HasMaxLength(200);

        builder.Property(s => s.DossierRemindersEnabled).HasDefaultValue(true);
        builder.Property(s => s.DossierReminderDays).HasDefaultValue(7);
        builder.Property(s => s.DossierEscalationDays).HasDefaultValue(30);

        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class ExpiryReminderPolicyConfiguration : IEntityTypeConfiguration<ExpiryReminderPolicy>
{
    public void Configure(EntityTypeBuilder<ExpiryReminderPolicy> builder)
    {
        builder.ToTable("expiry_reminder_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TargetKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.TargetCode).IsRequired().HasMaxLength(100);

        builder.HasIndex(p => new { p.TenantId, p.TargetKind, p.TargetCode })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class ReminderDispatchLogConfiguration : IEntityTypeConfiguration<ReminderDispatchLog>
{
    public void Configure(EntityTypeBuilder<ReminderDispatchLog> builder)
    {
        builder.ToTable("reminder_dispatch_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.DedupeKey).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Kind).IsRequired().HasMaxLength(50);

        builder.HasIndex(l => new { l.TenantId, l.DedupeKey }).IsUnique();
        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
