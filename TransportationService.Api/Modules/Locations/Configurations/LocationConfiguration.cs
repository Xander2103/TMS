using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Code).IsRequired().HasMaxLength(40);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(30);

        builder.Property(l => l.Street).HasMaxLength(150);
        builder.Property(l => l.HouseNumber).HasMaxLength(20);
        builder.Property(l => l.PostalCode).HasMaxLength(20);
        builder.Property(l => l.City).HasMaxLength(100);
        builder.Property(l => l.CountryCode).HasMaxLength(2);

        builder.Property(l => l.Latitude).HasPrecision(9, 6);
        builder.Property(l => l.Longitude).HasPrecision(9, 6);

        builder.Property(l => l.ContactName).HasMaxLength(150);
        builder.Property(l => l.ContactPhone).HasMaxLength(30);
        builder.Property(l => l.ContactEmail).HasMaxLength(250);

        builder.Property(l => l.OpeningHours).HasMaxLength(500);
        builder.Property(l => l.LoadingInstructions).HasMaxLength(2000);
        builder.Property(l => l.UnloadingInstructions).HasMaxLength(2000);
        builder.Property(l => l.AccessInstructions).HasMaxLength(2000);
        builder.Property(l => l.AccessRestrictions).HasMaxLength(1000);
        builder.Property(l => l.VehicleRestrictions).HasMaxLength(1000);
        builder.Property(l => l.TrailerRestrictions).HasMaxLength(1000);
        builder.Property(l => l.Notes).HasMaxLength(2000);

        builder.HasIndex(l => new { l.TenantId, l.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(l => l.TenantId);
        builder.HasIndex(l => new { l.TenantId, l.Type });
        builder.HasIndex(l => new { l.TenantId, l.IsActive });
        builder.HasIndex(l => new { l.TenantId, l.CustomerId });

        // Business rule: at most one default loading and one default unloading location per
        // customer (soft-deleted rows excluded, matching the Code index filter style). The
        // string names give each index its own model identity — without them EF would treat
        // these as reconfigurations of the unnamed index above.
        builder.HasIndex(l => new { l.TenantId, l.CustomerId }, "IX_locations_default_loading_per_customer")
            .IsUnique()
            .HasFilter("\"IsDefaultLoadingLocation\" = true AND \"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.CustomerId }, "IX_locations_default_unloading_per_customer")
            .IsUnique()
            .HasFilter("\"IsDefaultUnloadingLocation\" = true AND \"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.CustomerId }, "IX_locations_default_billing_per_customer")
            .IsUnique()
            .HasFilter("\"IsDefaultBillingLocation\" = true AND \"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
