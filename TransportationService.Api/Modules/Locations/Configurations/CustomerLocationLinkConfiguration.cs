using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Configurations;

public class CustomerLocationLinkConfiguration : IEntityTypeConfiguration<CustomerLocationLink>
{
    public void Configure(EntityTypeBuilder<CustomerLocationLink> builder)
    {
        builder.ToTable("customer_location_links");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Alias).HasMaxLength(200);
        builder.Property(l => l.CustomerReference).HasMaxLength(100);
        builder.Property(l => l.Instructions).HasMaxLength(2000);

        // Removing the physical address cascades its relationships away; the address itself is
        // soft-deleted in practice, so this only fires on a hard delete.
        builder.HasOne(l => l.Location)
            .WithMany()
            .HasForeignKey(l => l.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TransportationService.Api.Modules.Partners.Entities.Customer>()
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.TenantId);
        builder.HasIndex(l => new { l.TenantId, l.CustomerId });
        builder.HasIndex(l => new { l.TenantId, l.LocationId });

        // One relationship row per customer/address pair.
        builder.HasIndex(l => new { l.TenantId, l.CustomerId, l.LocationId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // At most one default of each kind per customer. Named so EF gives each its own model
        // identity instead of treating them as reconfigurations of the index above (same style
        // as the legacy per-customer default indexes on locations).
        builder.HasIndex(l => new { l.TenantId, l.CustomerId }, "IX_customer_location_links_default_loading")
            .IsUnique()
            .HasFilter("\"IsDefaultLoading\" = true AND \"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.CustomerId }, "IX_customer_location_links_default_unloading")
            .IsUnique()
            .HasFilter("\"IsDefaultUnloading\" = true AND \"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.CustomerId }, "IX_customer_location_links_default_billing")
            .IsUnique()
            .HasFilter("\"IsDefaultBilling\" = true AND \"IsDeleted\" = false");

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
