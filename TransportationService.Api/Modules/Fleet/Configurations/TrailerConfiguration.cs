using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class TrailerConfiguration : IEntityTypeConfiguration<Trailer>
{
    public void Configure(EntityTypeBuilder<Trailer> builder)
    {
        builder.ToTable("trailers");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.InternalNumber).IsRequired().HasMaxLength(30);
        builder.Property(t => t.LicensePlate).IsRequired().HasMaxLength(20);
        builder.Property(t => t.Vin).HasMaxLength(50);
        builder.Property(t => t.Brand).HasMaxLength(100);
        builder.Property(t => t.Model).HasMaxLength(100);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.StatusReason).HasMaxLength(500);

        builder.Property(t => t.OwnershipType).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.OperationalStatus).HasConversion<string>().HasMaxLength(20);

        builder.Property(t => t.CapacityKg).HasPrecision(10, 2);
        builder.Property(t => t.LengthMeters).HasPrecision(6, 2);
        builder.Property(t => t.WidthMeters).HasPrecision(6, 2);
        builder.Property(t => t.HeightMeters).HasPrecision(6, 2);
        builder.Property(t => t.VolumeM3).HasPrecision(8, 2);

        builder.HasIndex(t => new { t.TenantId, t.InternalNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(t => new { t.TenantId, t.LicensePlate }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(t => t.TenantId);
        builder.HasIndex(t => new { t.TenantId, t.IsActive });
        builder.HasIndex(t => new { t.TenantId, t.OperationalStatus });

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}
