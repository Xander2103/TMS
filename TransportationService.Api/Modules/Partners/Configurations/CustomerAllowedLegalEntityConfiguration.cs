using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class CustomerAllowedLegalEntityConfiguration : IEntityTypeConfiguration<CustomerAllowedLegalEntity>
{
    public void Configure(EntityTypeBuilder<CustomerAllowedLegalEntity> builder)
    {
        builder.ToTable("customer_allowed_legal_entities");
        builder.HasKey(l => l.Id);

        builder.HasOne<Customer>().WithMany().HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization.Entities.LegalEntity>().WithMany().HasForeignKey(l => l.LegalEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        // Soft-deleted rows stay; uniqueness covers only the active pair.
        builder.HasIndex(l => new { l.CustomerId, l.LegalEntityId }, "UX_customer_allowed_entities_active")
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.CustomerId });

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
