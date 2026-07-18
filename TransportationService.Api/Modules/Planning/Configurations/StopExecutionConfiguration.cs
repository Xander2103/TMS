using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Configurations;

public class StopExecutionConfiguration : IEntityTypeConfiguration<StopExecution>
{
    public void Configure(EntityTypeBuilder<StopExecution> builder)
    {
        builder.ToTable("stop_executions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PodPath).HasMaxLength(300);
        builder.Property(e => e.PodSignedBy).HasMaxLength(200);
        builder.Property(e => e.Remarks).HasMaxLength(2000);

        builder.HasIndex(e => new { e.TripId, e.TransportOrderStopId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Trip>().WithMany().HasForeignKey(e => e.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(e => e.TransportOrderStopId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
