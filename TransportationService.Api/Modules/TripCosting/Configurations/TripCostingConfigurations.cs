using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.TripCosting.Configurations;

public class CostRateSetConfiguration : IEntityTypeConfiguration<CostRateSet>
{
    public void Configure(EntityTypeBuilder<CostRateSet> builder)
    {
        builder.ToTable("cost_rate_sets");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.FuelPricePerLitre).HasPrecision(8, 3);
        builder.Property(r => r.DefaultConsumptionLPer100Km).HasPrecision(6, 1);
        builder.Property(r => r.VehicleCostPerKm).HasPrecision(8, 2);
        builder.Property(r => r.VehicleCostPerHour).HasPrecision(8, 2);
        builder.Property(r => r.DriverCostPerHour).HasPrecision(8, 2);
        builder.Property(r => r.EmployerCostMultiplier).HasPrecision(5, 2);
        builder.Property(r => r.MaintenanceCostPerKm).HasPrecision(8, 3);
        builder.Property(r => r.DepreciationPerDay).HasPrecision(10, 2);
        builder.Property(r => r.TrailerCostPerDay).HasPrecision(10, 2);
        builder.Property(r => r.EquipmentCostPerDay).HasPrecision(10, 2);
        builder.Property(r => r.DefaultTollPerTrip).HasPrecision(10, 2);
        builder.Property(r => r.OvertimeRateMultiplier).HasPrecision(5, 2);
        builder.Property(r => r.WaitingTimeCostPerHour).HasPrecision(8, 2);
        builder.Property(r => r.Co2KgPerLitreDiesel).HasPrecision(6, 3);
        builder.Property(r => r.Co2KgPerLitreOther).HasPrecision(6, 3);

        builder.HasIndex(r => new { r.TenantId, r.EffectiveFrom })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class TripCostLineConfiguration : IEntityTypeConfiguration<TripCostLine>
{
    public void Configure(EntityTypeBuilder<TripCostLine> builder)
    {
        builder.ToTable("trip_cost_lines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Phase).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.CostType).HasConversion<string>().HasMaxLength(30);
        builder.Property(l => l.Description).IsRequired().HasMaxLength(300);
        builder.Property(l => l.Quantity).HasPrecision(12, 3);
        builder.Property(l => l.Unit).IsRequired().HasMaxLength(10);
        builder.Property(l => l.UnitRate).HasPrecision(12, 4);
        builder.Property(l => l.Amount).HasPrecision(12, 2);
        builder.Property(l => l.Source).IsRequired().HasMaxLength(20);
        builder.Property(l => l.OverrideReason).HasMaxLength(500);

        builder.HasIndex(l => new { l.TenantId, l.TripId, l.Phase });

        builder.HasOne<TransportationService.Api.Modules.Planning.Entities.Trip>()
            .WithMany()
            .HasForeignKey(l => l.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class TripCostSummaryConfiguration : IEntityTypeConfiguration<TripCostSummary>
{
    public void Configure(EntityTypeBuilder<TripCostSummary> builder)
    {
        builder.ToTable("trip_cost_summaries");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.EstimatedTotal).HasPrecision(12, 2);
        builder.Property(s => s.ActualTotal).HasPrecision(12, 2);
        builder.Property(s => s.ProjectedTotal).HasPrecision(12, 2);
        builder.Property(s => s.Revenue).HasPrecision(12, 2);
        builder.Property(s => s.FinalCost).HasPrecision(12, 2);
        builder.Property(s => s.FinalRevenue).HasPrecision(12, 2);

        builder.HasIndex(s => new { s.TenantId, s.TripId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<TransportationService.Api.Modules.Planning.Entities.Trip>()
            .WithMany()
            .HasForeignKey(s => s.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
