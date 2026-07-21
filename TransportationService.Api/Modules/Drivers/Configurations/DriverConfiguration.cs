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

        // A trailer being deleted merely clears the preference (SetNull) rather than blocking
        // the delete or removing the driver. Vehicle assignment lives on the Vehicle side only.
        builder.HasOne<Trailer>().WithMany().HasForeignKey(d => d.FixedTrailerId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(d => d.Categories)
            .WithOne()
            .HasForeignKey(c => c.DriverId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DriverDriverCategoryConfiguration : IEntityTypeConfiguration<DriverDriverCategory>
{
    public void Configure(EntityTypeBuilder<DriverDriverCategory> builder)
    {
        builder.ToTable("driver_driver_categories");
        builder.HasKey(c => c.Id);

        builder.HasIndex(c => new { c.TenantId, c.DriverId, c.DriverCategoryId }).IsUnique();

        builder.HasOne<DriverCategory>().WithMany()
            .HasForeignKey(c => c.DriverCategoryId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
