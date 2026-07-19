using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.EmployeePlanning.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Configurations;

public class TripPlanningEntryConfiguration : IEntityTypeConfiguration<TripPlanningEntry>
{
    public void Configure(EntityTypeBuilder<TripPlanningEntry> builder)
    {
        builder.ToTable("trip_planning_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SourceType).IsRequired().HasMaxLength(20);
        builder.Property(e => e.TripNumber).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.VehicleSummary).HasMaxLength(120);
        builder.Property(e => e.RouteSummary).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        // One live planning entry per trip — the duplicate backstop the sync service relies on.
        builder.HasIndex(e => new { e.TenantId, e.TripId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Date });
        builder.HasIndex(e => new { e.TenantId, e.Date });

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
