using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Orders.Configurations;

public class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public void Configure(EntityTypeBuilder<TransportOrder> builder)
    {
        builder.ToTable("transport_orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(30);
        builder.Property(o => o.CustomerReference).HasMaxLength(100);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.GoodsDescription).HasMaxLength(1000);
        builder.Property(o => o.Quantity).HasPrecision(12, 2);
        builder.Property(o => o.QuantityUnit).HasMaxLength(50);
        builder.Property(o => o.WeightKg).HasPrecision(12, 2);
        builder.Property(o => o.VolumeM3).HasPrecision(12, 2);
        builder.Property(o => o.AgreedPrice).HasPrecision(12, 2);
        builder.Property(o => o.Notes).HasMaxLength(4000);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        builder.HasIndex(o => new { o.TenantId, o.OrderNumber })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CustomerId });
        builder.HasIndex(o => new { o.TenantId, o.OrderDate });

        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Stops)
            .WithOne()
            .HasForeignKey(s => s.TransportOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}

public class TransportOrderStopConfiguration : IEntityTypeConfiguration<TransportOrderStop>
{
    public void Configure(EntityTypeBuilder<TransportOrderStop> builder)
    {
        builder.ToTable("transport_order_stops");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.StopType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.LocationName).HasMaxLength(200);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.PostalCode).HasMaxLength(20);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.CountryCode).HasMaxLength(2);
        builder.Property(s => s.Reference).HasMaxLength(100);
        builder.Property(s => s.Instructions).HasMaxLength(2000);
        builder.Property(s => s.AppointmentReference).HasMaxLength(100);
        builder.Property(s => s.AccessInstructions).HasMaxLength(2000);
        builder.Property(s => s.LoadingInstructions).HasMaxLength(2000);
        builder.Property(s => s.UnloadingInstructions).HasMaxLength(2000);

        builder.HasIndex(s => new { s.TransportOrderId, s.Sequence });

        builder.HasOne<Location>().WithMany().HasForeignKey(s => s.LocationId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
