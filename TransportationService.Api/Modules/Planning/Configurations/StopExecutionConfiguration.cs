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
        builder.Property(e => e.LateArrivalReason).HasMaxLength(500);
        builder.Property(e => e.StatusReason).HasMaxLength(500);

        builder.HasIndex(e => new { e.TripId, e.TransportOrderStopId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Trip>().WithMany().HasForeignKey(e => e.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(e => e.TransportOrderStopId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class StopStatusHistoryConfiguration : IEntityTypeConfiguration<StopStatusHistory>
{
    public void Configure(EntityTypeBuilder<StopStatusHistory> builder)
    {
        builder.ToTable("stop_status_histories");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(500);

        builder.HasIndex(e => new { e.StopExecutionId, e.OccurredAt });
        // Offline-replay idempotency: one transition per client key per tenant.
        builder.HasIndex(e => new { e.TenantId, e.ClientRequestId })
            .IsUnique()
            .HasFilter("\"ClientRequestId\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasOne<StopExecution>().WithMany().HasForeignKey(e => e.StopExecutionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
