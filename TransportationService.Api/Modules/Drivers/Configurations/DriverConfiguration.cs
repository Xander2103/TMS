using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Drivers.Configurations;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DriverNumber).IsRequired().HasMaxLength(30);
        builder.Property(d => d.AvailabilityStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.BlockReason).HasMaxLength(500);
        builder.Property(d => d.Notes).HasMaxLength(2000);

        // One driver per employee, and unique driver numbers - scoped per tenant and ignoring
        // soft-deleted rows so a number/employee can be reused after a driver is removed.
        builder.HasIndex(d => new { d.TenantId, d.DriverNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(d => d.TenantId);
        builder.HasIndex(d => new { d.TenantId, d.IsActive });

        builder.HasQueryFilter(d => !d.IsDeleted);

        // Forward references into Fleet, now that the Vehicle table exists. A vehicle being
        // deleted merely clears the preference/default (SetNull) rather than blocking the delete
        // or removing the driver.
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.DefaultVehicleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.PreferredVehicleId).OnDelete(DeleteBehavior.SetNull);
    }
}
