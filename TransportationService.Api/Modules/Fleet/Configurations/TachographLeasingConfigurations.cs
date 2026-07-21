using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class TachographCalibrationConfiguration : IEntityTypeConfiguration<TachographCalibration>
{
    public void Configure(EntityTypeBuilder<TachographCalibration> builder)
    {
        builder.ToTable("tachograph_calibrations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TachographType).HasMaxLength(50);
        builder.Property(t => t.Manufacturer).HasMaxLength(100);
        builder.Property(t => t.Model).HasMaxLength(100);
        builder.Property(t => t.SerialNumber).HasMaxLength(100);
        builder.Property(t => t.Workshop).HasMaxLength(150);
        builder.Property(t => t.CertificateNumber).HasMaxLength(100);
        builder.Property(t => t.SealReference).HasMaxLength(100);
        builder.Property(t => t.StorageKey).HasMaxLength(500);
        builder.Property(t => t.FileName).HasMaxLength(255);
        builder.Property(t => t.ContentType).HasMaxLength(100);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        builder.HasIndex(t => new { t.TenantId, t.VehicleId });

        builder.HasOne<Vehicle>().WithMany()
            .HasForeignKey(t => t.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class LeasingContractConfiguration : IEntityTypeConfiguration<LeasingContract>
{
    public void Configure(EntityTypeBuilder<LeasingContract> builder)
    {
        builder.ToTable("leasing_contracts");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.LeasingCompany).IsRequired().HasMaxLength(150);
        builder.Property(l => l.ContractNumber).HasMaxLength(100);
        builder.Property(l => l.MonthlyAmount).HasPrecision(12, 2);
        builder.Property(l => l.Currency).IsRequired().HasMaxLength(3);
        builder.Property(l => l.ContactPerson).HasMaxLength(150);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.Property(l => l.StorageKey).HasMaxLength(500);
        builder.Property(l => l.FileName).HasMaxLength(255);
        builder.Property(l => l.ContentType).HasMaxLength(100);

        builder.HasIndex(l => new { l.TenantId, l.VehicleId });
        builder.HasIndex(l => new { l.TenantId, l.TrailerId });

        // Exactly one of VehicleId/TrailerId is set.
        builder.ToTable(t => t.HasCheckConstraint("ck_leasing_owner",
            "(\"VehicleId\" IS NOT NULL AND \"TrailerId\" IS NULL) OR (\"VehicleId\" IS NULL AND \"TrailerId\" IS NOT NULL)"));

        builder.HasOne<Vehicle>().WithMany()
            .HasForeignKey(l => l.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Trailer>().WithMany()
            .HasForeignKey(l => l.TrailerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
