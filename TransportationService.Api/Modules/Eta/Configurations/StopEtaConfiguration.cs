using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Eta.Configurations;

public class StopEtaConfiguration : IEntityTypeConfiguration<StopEta>
{
    public void Configure(EntityTypeBuilder<StopEta> builder)
    {
        builder.ToTable("stop_etas");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(500);

        builder.HasIndex(e => new { e.TripId, e.TransportOrderStopId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Trip>().WithMany().HasForeignKey(e => e.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(e => e.TransportOrderStopId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class StopEtaHistoryConfiguration : IEntityTypeConfiguration<StopEtaHistory>
{
    public void Configure(EntityTypeBuilder<StopEtaHistory> builder)
    {
        builder.ToTable("stop_eta_histories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Source).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(h => new { h.StopEtaId, h.RecordedAt });

        builder.HasOne<StopEta>().WithMany().HasForeignKey(h => h.StopEtaId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(h => !h.IsDeleted);
    }
}
