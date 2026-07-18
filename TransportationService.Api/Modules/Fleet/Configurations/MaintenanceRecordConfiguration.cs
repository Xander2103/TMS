using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> builder)
    {
        builder.ToTable("maintenance_records", table =>
            table.HasCheckConstraint(
                "CK_maintenance_records_single_owner",
                "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)"));

        builder.HasKey(m => m.Id);

        builder.Property(m => m.MaintenanceType).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.CustomTypeName).HasMaxLength(100);
        builder.Property(m => m.Description).IsRequired().HasMaxLength(500);
        builder.Property(m => m.WorkPerformed).HasMaxLength(2000);
        builder.Property(m => m.Provider).HasMaxLength(200);
        builder.Property(m => m.Cost).HasPrecision(12, 2);
        builder.Property(m => m.AttachmentPath).HasMaxLength(300);
        builder.Property(m => m.Notes).HasMaxLength(2000);

        builder.HasIndex(m => new { m.TenantId, m.VehicleId });
        builder.HasIndex(m => new { m.TenantId, m.TrailerId });
        builder.HasIndex(m => new { m.TenantId, m.Status, m.ScheduledDate });

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Trailer>().WithMany().HasForeignKey(m => m.TrailerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
