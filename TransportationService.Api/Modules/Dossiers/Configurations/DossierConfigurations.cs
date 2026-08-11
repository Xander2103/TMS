using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Dossiers.Configurations;

public class TransportDossierConfiguration : IEntityTypeConfiguration<TransportDossier>
{
    public void Configure(EntityTypeBuilder<TransportDossier> builder)
    {
        builder.ToTable("transport_dossiers");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DossierNumber).HasMaxLength(30).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2000);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Notes).HasMaxLength(4000);
        builder.Property(d => d.CustomerReference).HasMaxLength(100);

        builder.HasOne<Organization.Entities.LegalEntity>().WithMany().HasForeignKey(d => d.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(d => d.OriginTransportOrderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(d => d.Activities).WithOne().HasForeignKey(a => a.DossierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.TenantId, d.DossierNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(d => new { d.TenantId, d.Status });
        builder.HasIndex(d => new { d.TenantId, d.CustomerId });
        // Idempotency key of the order-wrapper backfill/auto-wrap: at most one wrapper per order, ever.
        builder.HasIndex(d => d.OriginTransportOrderId, "UX_transport_dossiers_origin_order")
            .IsUnique()
            .HasFilter("\"OriginTransportOrderId\" IS NOT NULL");

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class ActivityTypeConfiguration : IEntityTypeConfiguration<ActivityType>
{
    public void Configure(EntityTypeBuilder<ActivityType> builder)
    {
        builder.ToTable("activity_types");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Icon).HasMaxLength(50);
        builder.Property(t => t.KpiCategory).HasMaxLength(50);

        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        // Mirrors LegalEntity.IsDefault: at most one active system-default transport type per tenant.
        builder.HasIndex(t => t.TenantId, "UX_activity_types_default_transport")
            .IsUnique()
            .HasFilter("\"IsSystemDefaultTransport\" = true AND \"IsActive\" = true AND \"IsDeleted\" = false");

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class DossierActivityConfiguration : IEntityTypeConfiguration<DossierActivity>
{
    public void Configure(EntityTypeBuilder<DossierActivity> builder)
    {
        builder.ToTable("dossier_activities");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.Property(a => a.DurationHours).HasPrecision(8, 2);

        builder.HasOne(a => a.ActivityType).WithMany().HasForeignKey(a => a.ActivityTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(a => a.LinkedTransportOrderId)
            .OnDelete(DeleteBehavior.SetNull);
        // Restrict avoids a second cascade path via the dossier; the service clears
        // accompaniment links before an activity is removed.
        builder.HasOne<DossierActivity>().WithMany().HasForeignKey(a => a.LinkedActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.TenantId, a.DossierId });
        builder.HasIndex(a => new { a.TenantId, a.ActivityTypeId });
        builder.HasIndex(a => new { a.TenantId, a.LinkedTransportOrderId })
            .HasFilter("\"LinkedTransportOrderId\" IS NOT NULL");

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class DossierOrderConfiguration : IEntityTypeConfiguration<DossierOrder>
{
    public void Configure(EntityTypeBuilder<DossierOrder> builder)
    {
        builder.ToTable("dossier_orders");
        builder.HasKey(l => l.Id);

        builder.HasOne<TransportDossier>().WithMany().HasForeignKey(l => l.DossierId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(l => l.TransportOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Soft-deleted links stay in the table, so uniqueness only covers the active link.
        builder.HasIndex(l => new { l.DossierId, l.TransportOrderId }, "UX_dossier_orders_active_link")
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(l => new { l.TenantId, l.TransportOrderId });

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class DossierRelationConfiguration : IEntityTypeConfiguration<DossierRelation>
{
    public void Configure(EntityTypeBuilder<DossierRelation> builder)
    {
        builder.ToTable("dossier_relations", table =>
            table.HasCheckConstraint("CK_dossier_relations_no_self_link",
                "\"SourceDossierId\" <> \"TargetDossierId\""));
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RelationType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.HasOne<TransportDossier>().WithMany().HasForeignKey(r => r.SourceDossierId)
            .OnDelete(DeleteBehavior.Cascade);
        // Restrict on the target avoids a second cascade path; the service removes relations
        // in both directions before a dossier itself is deleted.
        builder.HasOne<TransportDossier>().WithMany().HasForeignKey(r => r.TargetDossierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.SourceDossierId, r.TargetDossierId, r.RelationType }, "UX_dossier_relations_active")
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(r => new { r.TenantId, r.TargetDossierId });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
