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

        builder.HasIndex(d => new { d.TenantId, d.DossierNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(d => new { d.TenantId, d.Status });
        builder.HasIndex(d => new { d.TenantId, d.CustomerId });

        builder.HasQueryFilter(d => !d.IsDeleted);
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
