using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Entities;

namespace TransportationService.Api.Modules.Pod.Configurations;

public class ProofOfDeliveryConfiguration : IEntityTypeConfiguration<ProofOfDelivery>
{
    public void Configure(EntityTypeBuilder<ProofOfDelivery> builder)
    {
        builder.ToTable("proofs_of_delivery");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.RecipientName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.RecipientRole).HasMaxLength(100);
        builder.Property(p => p.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);
        builder.Property(p => p.SignaturePath).HasMaxLength(300);
        builder.Property(p => p.CorrectionReason).HasMaxLength(500);

        // One current proof per stop within a trip; superseded versions stay behind it.
        builder.HasIndex(p => new { p.TripId, p.TransportOrderStopId })
            .IsUnique()
            .HasFilter("\"IsCurrent\" = true AND \"IsDeleted\" = false");
        builder.HasIndex(p => new { p.TenantId, p.TransportOrderId });

        builder.HasOne<Trip>().WithMany().HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(p => p.TransportOrderStopId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProofOfDelivery>().WithMany().HasForeignKey(p => p.CorrectedFromPodId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Photos)
            .WithOne()
            .HasForeignKey(x => x.ProofOfDeliveryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class PodPhotoConfiguration : IEntityTypeConfiguration<PodPhoto>
{
    public void Configure(EntityTypeBuilder<PodPhoto> builder)
    {
        builder.ToTable("pod_photos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.FileName).IsRequired().HasMaxLength(255);
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.StoragePath).IsRequired().HasMaxLength(300);

        builder.HasIndex(p => p.ProofOfDeliveryId);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
