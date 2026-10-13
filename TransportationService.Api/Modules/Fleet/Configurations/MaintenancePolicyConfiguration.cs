using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class MaintenancePolicyConfiguration : IEntityTypeConfiguration<MaintenancePolicy>
{
    public void Configure(EntityTypeBuilder<MaintenancePolicy> builder)
    {
        builder.ToTable("maintenance_policies", table =>
            // A policy targets at most one level: an asset OR a category OR the company default.
            table.HasCheckConstraint("CK_maintenance_policies_single_level",
                "(CASE WHEN \"VehicleId\" IS NOT NULL THEN 1 ELSE 0 END"
                + " + CASE WHEN \"TrailerId\" IS NOT NULL THEN 1 ELSE 0 END"
                + " + CASE WHEN \"CategoryId\" IS NOT NULL THEN 1 ELSE 0 END) <= 1"));
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.AssetKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Description).HasMaxLength(500);

        builder.HasIndex(p => new { p.TenantId, p.AssetKind, p.Kind });
        builder.HasIndex(p => new { p.TenantId, p.VehicleId });
        builder.HasIndex(p => new { p.TenantId, p.TrailerId });

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
