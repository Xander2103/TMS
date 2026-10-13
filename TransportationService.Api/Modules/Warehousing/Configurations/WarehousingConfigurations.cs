using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Warehousing.Entities;

namespace TransportationService.Api.Modules.Warehousing.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);
        builder.Property(w => w.ContactName).HasMaxLength(200);
        builder.Property(w => w.ContactPhone).HasMaxLength(50);
        builder.Property(w => w.ContactEmail).HasMaxLength(200);
        builder.Property(w => w.Notes).HasMaxLength(2000);

        builder.HasIndex(w => new { w.TenantId, w.Name })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Location>().WithMany().HasForeignKey(w => w.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(w => w.Docks).WithOne().HasForeignKey(d => d.WarehouseId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(w => !w.IsDeleted);
    }
}

public class DockConfiguration : IEntityTypeConfiguration<Dock>
{
    public void Configure(EntityTypeBuilder<Dock> builder)
    {
        builder.ToTable("docks");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).IsRequired().HasMaxLength(30);
        builder.Property(d => d.Name).HasMaxLength(100);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.Property(d => d.MaxVehicleLengthM).HasPrecision(6, 2);
        builder.Property(d => d.MaxVehicleHeightM).HasPrecision(6, 2);

        builder.HasIndex(d => new { d.TenantId, d.WarehouseId, d.Code })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class DockAppointmentConfiguration : IEntityTypeConfiguration<DockAppointment>
{
    public void Configure(EntityTypeBuilder<DockAppointment> builder)
    {
        builder.ToTable("dock_appointments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.OperationType).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Priority).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.Reference).HasMaxLength(100);
        builder.Property(a => a.Remarks).HasMaxLength(2000);
        // Race backstop on top of the explicit service-level version check.
        builder.Property(a => a.Version).IsConcurrencyToken();

        builder.HasIndex(a => new { a.TenantId, a.WarehouseId, a.PlannedStart });
        builder.HasIndex(a => new { a.TenantId, a.DockId, a.PlannedStart });
        builder.HasIndex(a => new { a.TenantId, a.Status });

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Dock>().WithMany().HasForeignKey(a => a.DockId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Trip>().WithMany().HasForeignKey(a => a.TripId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(a => a.TransportOrderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Trailer>().WithMany().HasForeignKey(a => a.TrailerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Driver>().WithMany().HasForeignKey(a => a.DriverId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class WarehouseLocationConfiguration : IEntityTypeConfiguration<WarehouseLocation>
{
    public void Configure(EntityTypeBuilder<WarehouseLocation> builder)
    {
        builder.ToTable("warehouse_locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Kind).IsRequired().HasMaxLength(20);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(l => l.WarehouseId).OnDelete(DeleteBehavior.Cascade);
        // Self-FK Restrict: a zone with positions must be emptied first (enforced in the service too).
        builder.HasOne<WarehouseLocation>().WithMany().HasForeignKey(l => l.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.TenantId, l.WarehouseId, l.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class StorageStayConfiguration : IEntityTypeConfiguration<StorageStay>
{
    public void Configure(EntityTypeBuilder<StorageStay> builder)
    {
        builder.ToTable("storage_stays");
        builder.HasKey(s => s.Id);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.TenantId, s.PackageId });
        builder.HasIndex(s => new { s.TenantId, s.WarehouseId, s.OutAt });
        // At most one OPEN stay per package.
        builder.HasIndex(s => new { s.TenantId, s.PackageId })
            .HasFilter("\"OutAt\" IS NULL AND \"IsDeleted\" = false")
            .IsUnique()
            .HasDatabaseName("UX_storage_stays_open_per_package");
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
