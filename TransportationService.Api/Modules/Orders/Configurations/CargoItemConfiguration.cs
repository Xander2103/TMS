using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Configurations;

public class CargoItemConfiguration : IEntityTypeConfiguration<CargoItem>
{
    public void Configure(EntityTypeBuilder<CargoItem> builder)
    {
        builder.ToTable("order_cargo_items");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Description).IsRequired().HasMaxLength(300);
        builder.Property(c => c.Barcode).HasMaxLength(100);
        builder.Property(c => c.ExpectedQuantity).HasPrecision(12, 2);
        builder.Property(c => c.QuantityUnit).HasMaxLength(50);
        builder.Property(c => c.Notes).HasMaxLength(500);

        builder.Property(c => c.UnitType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.UnitTypeLabel).HasMaxLength(50);
        builder.Property(c => c.TotalWeightKg).HasPrecision(12, 2);
        builder.Property(c => c.WeightPerUnitKg).HasPrecision(12, 3);
        builder.Property(c => c.LengthMeters).HasPrecision(8, 3);
        builder.Property(c => c.WidthMeters).HasPrecision(8, 3);
        builder.Property(c => c.HeightMeters).HasPrecision(8, 3);
        builder.Property(c => c.VolumeM3).HasPrecision(12, 3);
        builder.Property(c => c.AdrDetails).HasMaxLength(500);
        builder.Property(c => c.Reference).HasMaxLength(100);

        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(c => c.LoadingStopId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(c => c.UnloadingStopId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => new { c.TransportOrderId, c.Sequence });

        // A barcode may recur across orders (EAN reuse) but must stay unambiguous within one order.
        builder.HasIndex(c => new { c.TransportOrderId, c.Barcode })
            .IsUnique()
            .HasFilter("\"Barcode\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(c => c.TransportOrderId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
