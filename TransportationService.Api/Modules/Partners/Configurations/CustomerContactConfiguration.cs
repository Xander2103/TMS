using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("customer_contacts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.LastName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.DisplayName).HasMaxLength(150);
        builder.Property(c => c.Nickname).HasMaxLength(100);
        builder.Property(c => c.Role).HasMaxLength(100);
        builder.Property(c => c.Email).HasMaxLength(250);
        builder.Property(c => c.PhoneNumber).HasMaxLength(30);
        builder.Property(c => c.MobilePhone).HasMaxLength(30);
        builder.Property(c => c.PreferredLanguageCode).HasMaxLength(10);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.ContactType).HasConversion<string>().HasMaxLength(30);

        builder.HasOne<ContactDepartment>().WithMany()
            .HasForeignKey(c => c.DepartmentId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.SetNull);

        builder.HasIndex(c => new { c.TenantId, c.CustomerId });

        // One primary contact per (customer, type); soft-deleted rows don't count.
        builder.HasIndex(c => new { c.TenantId, c.CustomerId, c.ContactType })
            .IsUnique()
            .HasFilter("\"IsPrimary\" = true AND \"IsDeleted\" = false")
            .HasDatabaseName("ix_customer_contacts_primary_per_type");

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
