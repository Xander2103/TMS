using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class TankCardConfiguration : IEntityTypeConfiguration<TankCard>
{
    public void Configure(EntityTypeBuilder<TankCard> builder)
    {
        builder.ToTable("tank_cards");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CardNumber).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Provider).IsRequired().HasMaxLength(100);
        builder.Property(c => c.BlockedReason).HasMaxLength(500);
        builder.Property(c => c.InternalName).HasMaxLength(200);
        builder.Property(c => c.FuelType).HasMaxLength(50);
        builder.Property(c => c.CostCenter).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(2000);

        builder.HasIndex(c => new { c.TenantId, c.CardNumber })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(c => new { c.TenantId, c.VehicleId });
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId });

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(c => c.VehicleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Driver>().WithMany().HasForeignKey(c => c.DriverId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
