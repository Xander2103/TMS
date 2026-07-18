using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class FleetDocumentConfiguration : IEntityTypeConfiguration<FleetDocument>
{
    public void Configure(EntityTypeBuilder<FleetDocument> builder)
    {
        builder.ToTable("fleet_documents", table =>
            // Exactly one owner: vehicle XOR trailer.
            table.HasCheckConstraint(
                "CK_fleet_documents_single_owner",
                "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.CustomTypeName).HasMaxLength(100);
        builder.Property(d => d.DocumentNumber).HasMaxLength(100);
        builder.Property(d => d.DocumentPath).HasMaxLength(300);
        builder.Property(d => d.Notes).HasMaxLength(2000);

        builder.HasIndex(d => new { d.TenantId, d.VehicleId });
        builder.HasIndex(d => new { d.TenantId, d.TrailerId });
        builder.HasIndex(d => new { d.TenantId, d.ExpiryDate });

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Trailer>().WithMany().HasForeignKey(d => d.TrailerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
