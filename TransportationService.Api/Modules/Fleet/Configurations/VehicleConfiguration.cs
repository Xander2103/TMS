using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.InternalNumber).IsRequired().HasMaxLength(30);
        builder.Property(v => v.LicensePlate).IsRequired().HasMaxLength(20);
        builder.Property(v => v.Vin).HasMaxLength(50);
        builder.Property(v => v.Brand).HasMaxLength(100);
        builder.Property(v => v.Model).HasMaxLength(100);
        builder.Property(v => v.Notes).HasMaxLength(2000);
        builder.Property(v => v.StatusReason).HasMaxLength(500);

        builder.Property(v => v.FuelType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.EmissionClass).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.OwnershipType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.OperationalStatus).HasConversion<string>().HasMaxLength(20);

        builder.Property(v => v.GrossVehicleWeightKg).HasPrecision(10, 2);
        builder.Property(v => v.PayloadKg).HasPrecision(10, 2);
        builder.Property(v => v.LengthMeters).HasPrecision(6, 2);
        builder.Property(v => v.WidthMeters).HasPrecision(6, 2);
        builder.Property(v => v.HeightMeters).HasPrecision(6, 2);
        builder.Property(v => v.VolumeM3).HasPrecision(8, 2);

        builder.HasIndex(v => new { v.TenantId, v.InternalNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(v => new { v.TenantId, v.LicensePlate }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(v => v.TenantId);
        builder.HasIndex(v => new { v.TenantId, v.IsActive });
        builder.HasIndex(v => new { v.TenantId, v.OperationalStatus });

        // A driver holds at most ONE vehicle per slot (fixed/current) per tenant. These filtered
        // unique indexes are the database backstop for the assignment service's invariant.
        builder.HasIndex(v => new { v.TenantId, v.FixedDriverId })
            .IsUnique()
            .HasFilter("\"FixedDriverId\" IS NOT NULL AND \"IsDeleted\" = false");
        builder.HasIndex(v => new { v.TenantId, v.CurrentDriverId })
            .IsUnique()
            .HasFilter("\"CurrentDriverId\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasQueryFilter(v => !v.IsDeleted);

        // A driver being deleted clears the assignment rather than blocking the delete.
        builder.HasOne<Driver>().WithMany().HasForeignKey(v => v.FixedDriverId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Driver>().WithMany().HasForeignKey(v => v.CurrentDriverId).OnDelete(DeleteBehavior.SetNull);
    }
}
