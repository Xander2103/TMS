using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("trips");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TripNumber).IsRequired().HasMaxLength(30);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Notes).HasMaxLength(4000);
        builder.Property(t => t.PlannedDistanceKm).HasPrecision(8, 1);
        builder.Property(t => t.PlannedEmptyKm).HasPrecision(8, 1);
        builder.Property(t => t.ActualDistanceKm).HasPrecision(8, 1);
        builder.Property(t => t.ActualEmptyKm).HasPrecision(8, 1);

        builder.HasIndex(t => new { t.TenantId, t.TripNumber })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(t => new { t.TenantId, t.TripDate });
        builder.HasIndex(t => new { t.TenantId, t.DriverId, t.TripDate });
        builder.HasIndex(t => new { t.TenantId, t.VehicleId, t.TripDate });

        builder.HasOne<Driver>().WithMany().HasForeignKey(t => t.DriverId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(t => t.VehicleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Trailer>().WithMany().HasForeignKey(t => t.TrailerId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Orders)
            .WithOne()
            .HasForeignKey(o => o.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class TripOrderConfiguration : IEntityTypeConfiguration<TripOrder>
{
    public void Configure(EntityTypeBuilder<TripOrder> builder)
    {
        builder.ToTable("trip_orders");

        builder.HasKey(o => o.Id);

        builder.HasIndex(o => new { o.TripId, o.Sequence });
        builder.HasIndex(o => new { o.TenantId, o.TransportOrderId });

        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(o => o.TransportOrderId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}
