using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Incidents.Entities;

namespace TransportationService.Api.Modules.Incidents.Configurations;

public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.IncidentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(20);

        builder.Property(i => i.CustomTypeName).HasMaxLength(100);
        builder.Property(i => i.Title).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(4000).IsRequired();
        builder.Property(i => i.Cause).HasMaxLength(2000);
        builder.Property(i => i.CustomerImpact).HasMaxLength(1000);
        builder.Property(i => i.OperationalImpact).HasMaxLength(1000);
        builder.Property(i => i.FinancialImpact).HasMaxLength(1000);
        builder.Property(i => i.EstimatedCost).HasPrecision(12, 2);
        builder.Property(i => i.ActualCost).HasPrecision(12, 2);
        builder.Property(i => i.Resolution).HasMaxLength(4000);

        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.DossierId });
        builder.HasIndex(i => new { i.TenantId, i.CustomerId });
        builder.HasIndex(i => new { i.TenantId, i.TransportOrderId });

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
