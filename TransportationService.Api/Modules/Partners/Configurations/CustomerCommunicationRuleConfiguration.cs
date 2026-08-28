using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class CustomerCommunicationRuleConfiguration : IEntityTypeConfiguration<CustomerCommunicationRule>
{
    public void Configure(EntityTypeBuilder<CustomerCommunicationRule> builder)
    {
        builder.ToTable("customer_communication_rules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.CustomTypeLabel).HasMaxLength(100);
        builder.Property(r => r.Channel).IsRequired().HasMaxLength(20);
        builder.Property(r => r.CcEmail).HasMaxLength(250);
        builder.Property(r => r.LanguageCode).HasMaxLength(10);

        builder.HasIndex(r => new { r.TenantId, r.CustomerId });
        builder.HasIndex(r => new { r.TenantId, r.CustomerId, r.Type });

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CustomerContact>().WithMany()
            .HasForeignKey(r => r.FallbackContactId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.Contacts).WithOne()
            .HasForeignKey(c => c.RuleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class CustomerCommunicationRuleContactConfiguration : IEntityTypeConfiguration<CustomerCommunicationRuleContact>
{
    public void Configure(EntityTypeBuilder<CustomerCommunicationRuleContact> builder)
    {
        builder.ToTable("customer_communication_rule_contacts");
        builder.HasKey(c => c.Id);

        // Unticking a contact soft-deletes the link; the index must not block re-ticking it.
        builder.HasIndex(c => new { c.TenantId, c.RuleId, c.ContactId }).IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<CustomerContact>().WithMany()
            .HasForeignKey(c => c.ContactId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
