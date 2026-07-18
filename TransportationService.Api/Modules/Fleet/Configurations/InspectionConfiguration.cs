using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections", table =>
            table.HasCheckConstraint(
                "CK_inspections_single_owner",
                "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)"));

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InspectionType).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.Result).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.CustomTypeName).HasMaxLength(100);
        builder.Property(i => i.AttachmentPath).HasMaxLength(300);
        builder.Property(i => i.Notes).HasMaxLength(2000);

        builder.HasIndex(i => new { i.TenantId, i.VehicleId });
        builder.HasIndex(i => new { i.TenantId, i.TrailerId });
        builder.HasIndex(i => new { i.TenantId, i.DueDate });

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(i => i.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Trailer>().WithMany().HasForeignKey(i => i.TrailerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
