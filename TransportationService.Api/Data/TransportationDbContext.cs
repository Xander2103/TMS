using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Models;

namespace TransportationService.Api.Data;

public class TransportationDbContext : DbContext
{
    public TransportationDbContext(DbContextOptions<TransportationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransportOrder>(entity =>
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
        });
    }
}
