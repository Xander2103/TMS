using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class FuelTransactionConfiguration : IEntityTypeConfiguration<FuelTransaction>
{
    public void Configure(EntityTypeBuilder<FuelTransaction> builder)
    {
        builder.ToTable("fuel_transactions");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Litres).HasPrecision(9, 2);
        builder.Property(f => f.TotalAmount).HasPrecision(12, 2);
        builder.Property(f => f.Station).HasMaxLength(200);
        builder.Property(f => f.Notes).HasMaxLength(2000);

        builder.HasIndex(f => new { f.TenantId, f.VehicleId, f.TransactionDate });
        builder.HasIndex(f => new { f.TenantId, f.TransactionDate });

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(f => f.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Driver>().WithMany().HasForeignKey(f => f.DriverId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TankCard>().WithMany().HasForeignKey(f => f.TankCardId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(f => !f.IsDeleted);
    }
}
