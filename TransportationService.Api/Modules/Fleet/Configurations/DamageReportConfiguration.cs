using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class DamageReportConfiguration : IEntityTypeConfiguration<DamageReport>
{
    public void Configure(EntityTypeBuilder<DamageReport> builder)
    {
        builder.ToTable("damage_reports", table =>
            table.HasCheckConstraint(
                "CK_damage_reports_single_owner",
                "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Location).HasMaxLength(300);
        builder.Property(d => d.Description).IsRequired().HasMaxLength(2000);
        builder.Property(d => d.InsuranceReference).HasMaxLength(100);
        builder.Property(d => d.RepairCost).HasPrecision(12, 2);
        builder.Property(d => d.AttachmentPath).HasMaxLength(300);
        builder.Property(d => d.Notes).HasMaxLength(2000);

        builder.HasIndex(d => new { d.TenantId, d.VehicleId });
        builder.HasIndex(d => new { d.TenantId, d.TrailerId });
        builder.HasIndex(d => new { d.TenantId, d.IncidentDate });

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Trailer>().WithMany().HasForeignKey(d => d.TrailerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Driver>().WithMany().HasForeignKey(d => d.DriverId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
