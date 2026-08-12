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
        builder.Property(i => i.ResponsibleParty).HasMaxLength(20);
        builder.Property(i => i.ResponsibilityNotes).HasMaxLength(2000);
        builder.Property(i => i.ChargeDecision).HasMaxLength(20);
        builder.Property(i => i.ChargeAmount).HasPrecision(12, 2);
        builder.Property(i => i.ChargeDescription).HasMaxLength(500);
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
        // Offline-replay idempotency: one incident per client key per tenant.
        builder.HasIndex(i => new { i.TenantId, i.ClientRequestId })
            .IsUnique()
            .HasFilter("\"ClientRequestId\" IS NOT NULL AND \"IsDeleted\" = false");

        // P4: at most one auto-created incident per failed stop, however often events replay.
        builder.HasIndex(i => new { i.TenantId, i.SourceStopId })
            .IsUnique()
            .HasFilter("\"SourceStopId\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}

public class IncidentChargePolicyConfiguration : IEntityTypeConfiguration<IncidentChargePolicy>
{
    public void Configure(EntityTypeBuilder<IncidentChargePolicy> builder)
    {
        builder.ToTable("incident_charge_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.IncidentType).HasMaxLength(30);
        builder.Property(p => p.Mode).IsRequired().HasMaxLength(20);
        builder.Property(p => p.DefaultAmount).HasPrecision(12, 2);
        builder.Property(p => p.DefaultDescription).HasMaxLength(500);
        builder.HasIndex(p => new { p.TenantId, p.CustomerId, p.IncidentType });
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
