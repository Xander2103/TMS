using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Packages.Entities;

namespace TransportationService.Api.Modules.Packages.Configurations;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("packages");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PackageNumber).IsRequired().HasMaxLength(40);
        builder.Property(p => p.BarcodeValue).IsRequired().HasMaxLength(100);
        builder.Property(p => p.BarcodeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ExternalBarcode).HasMaxLength(100);
        builder.Property(p => p.ExternalPackageReference).HasMaxLength(100);
        builder.Property(p => p.CustomerReference).HasMaxLength(100);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Quantity).HasPrecision(12, 2);
        builder.Property(p => p.UnitType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.UnitTypeLabel).HasMaxLength(50);
        builder.Property(p => p.WeightKg).HasPrecision(10, 2);
        builder.Property(p => p.VolumeM3).HasPrecision(10, 3);
        builder.Property(p => p.LengthCm).HasPrecision(8, 1);
        builder.Property(p => p.WidthCm).HasPrecision(8, 1);
        builder.Property(p => p.HeightCm).HasPrecision(8, 1);
        builder.Property(p => p.CurrentLifecycleStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.CurrentExceptionStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => new { p.TenantId, p.PackageNumber })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(p => new { p.TenantId, p.TransportOrderId });
        builder.HasIndex(p => new { p.TenantId, p.CurrentLifecycleStatus });
        builder.HasIndex(p => p.ParentPackageId);
        builder.HasIndex(p => p.CargoItemId);

        builder.HasOne<TransportationService.Api.Modules.Orders.Entities.TransportOrder>()
            .WithMany()
            .HasForeignKey(p => p.TransportOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportationService.Api.Modules.Orders.Entities.CargoItem>()
            .WithMany()
            .HasForeignKey(p => p.CargoItemId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TransportationService.Api.Modules.Orders.Entities.TransportOrderStop>()
            .WithMany()
            .HasForeignKey(p => p.LoadingStopId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TransportationService.Api.Modules.Orders.Entities.TransportOrderStop>()
            .WithMany()
            .HasForeignKey(p => p.DeliveryStopId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Package>()
            .WithMany()
            .HasForeignKey(p => p.ParentPackageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class PackageBarcodeConfiguration : IEntityTypeConfiguration<PackageBarcode>
{
    public void Configure(EntityTypeBuilder<PackageBarcode> builder)
    {
        builder.ToTable("package_barcodes");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Value).IsRequired().HasMaxLength(100);
        builder.Property(b => b.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.RetireReason).HasMaxLength(500);

        // Tenant-wide duplicate protection by constraint — the core scan-resolution guarantee.
        builder.HasIndex(b => new { b.TenantId, b.Value })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(b => b.PackageId);

        builder.HasOne<Package>()
            .WithMany()
            .HasForeignKey(b => b.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}

public class PackageLabelConfiguration : IEntityTypeConfiguration<PackageLabel>
{
    public void Configure(EntityTypeBuilder<PackageLabel> builder)
    {
        builder.ToTable("package_labels");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Format).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.SnapshotJson).IsRequired();
        builder.Property(l => l.ReprintReason).HasMaxLength(500);

        builder.HasIndex(l => new { l.TenantId, l.PackageId, l.Version })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Package>()
            .WithMany()
            .HasForeignKey(l => l.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class PackageEventConfiguration : IEntityTypeConfiguration<PackageEvent>
{
    public void Configure(EntityTypeBuilder<PackageEvent> builder)
    {
        builder.ToTable("package_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.OldStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.NewStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.DeviceInfo).HasMaxLength(200);
        builder.Property(e => e.BarcodeUsed).HasMaxLength(100);
        builder.Property(e => e.Result).HasMaxLength(40);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.Latitude).HasPrecision(9, 6);
        builder.Property(e => e.Longitude).HasPrecision(9, 6);

        builder.HasIndex(e => new { e.TenantId, e.PackageId, e.OccurredAt });
        builder.HasIndex(e => new { e.TenantId, e.TripId });

        builder.HasOne<Package>()
            .WithMany()
            .HasForeignKey(e => e.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
