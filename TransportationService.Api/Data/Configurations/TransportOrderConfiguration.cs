using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Models;

namespace TransportationService.Api.Data.Configurations;

public class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public void Configure(EntityTypeBuilder<TransportOrder> entity)
    {
        entity.ToTable("transport_orders");

        entity.HasKey(order => order.Id);

        entity.Property(order => order.Reference)
            .IsRequired()
            .HasMaxLength(50);

        entity.Property(order => order.CustomerName)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(order => order.PickupAddress)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(order => order.DeliveryAddress)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(order => order.Status)
            .IsRequired()
            .HasMaxLength(50);

        entity.HasIndex(order => order.Reference)
            .IsUnique();
    }
}
