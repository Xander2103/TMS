using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Scanning.Entities;

namespace TransportationService.Api.Modules.Scanning.Configurations;

public class ScanEventConfiguration : IEntityTypeConfiguration<ScanEvent>
{
    public void Configure(EntityTypeBuilder<ScanEvent> builder)
    {
        builder.ToTable("scan_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ScanType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Result).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Barcode).HasMaxLength(100);
        builder.Property(e => e.Quantity).HasPrecision(12, 2);
        builder.Property(e => e.DamageNote).HasMaxLength(500);
        builder.Property(e => e.CorrectionReason).HasMaxLength(500);
        builder.Property(e => e.DeviceInfo).HasMaxLength(200);

        builder.HasIndex(e => new { e.TenantId, e.TripId, e.OccurredAt });
        builder.HasIndex(e => new { e.TenantId, e.TransportOrderStopId });
        builder.HasIndex(e => e.CargoItemId);

        builder.HasOne<Trip>().WithMany().HasForeignKey(e => e.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(e => e.TransportOrderStopId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CargoItem>().WithMany().HasForeignKey(e => e.CargoItemId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
