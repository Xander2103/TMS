using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Configurations;

public class TransportOrderPricingLineConfiguration : IEntityTypeConfiguration<TransportOrderPricingLine>
{
    public void Configure(EntityTypeBuilder<TransportOrderPricingLine> builder)
    {
        builder.ToTable("order_pricing_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Label).IsRequired().HasMaxLength(300);
        builder.Property(l => l.Source).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Amount).HasPrecision(12, 2);
        builder.Property(l => l.RuleName).HasMaxLength(200);
        builder.Property(l => l.AgreementName).HasMaxLength(200);
        builder.Property(l => l.ActualQuantity).HasPrecision(12, 3);
        builder.Property(l => l.BillableQuantity).HasPrecision(12, 3);
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Quantity).HasPrecision(12, 3);
        builder.Property(l => l.UnitPrice).HasPrecision(14, 4);
        builder.Property(l => l.Unit).HasMaxLength(30);
        builder.Property(l => l.OriginalQuantity).HasPrecision(12, 3);
        builder.Property(l => l.OriginalUnitPrice).HasPrecision(14, 4);
        builder.Property(l => l.OriginalAmount).HasPrecision(12, 2);
        builder.Property(l => l.AdjustReason).HasMaxLength(500);
        builder.Property(l => l.LineKey).HasMaxLength(200);
        builder.HasIndex(l => new { l.TenantId, l.TransportOrderId });
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(l => l.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class TransportOrderPricingSnapshotConfiguration : IEntityTypeConfiguration<TransportOrderPricingSnapshot>
{
    public void Configure(EntityTypeBuilder<TransportOrderPricingSnapshot> builder)
    {
        builder.ToTable("order_pricing_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Currency).HasMaxLength(3);
        builder.Property(s => s.ZoneCode).HasMaxLength(30);
        builder.Property(s => s.ZoneName).HasMaxLength(150);
        builder.Property(s => s.AgreementNames).HasMaxLength(500);
        builder.Property(s => s.UnitSummary).HasMaxLength(300);
        builder.Property(s => s.CalculatedTotal).HasPrecision(12, 2);
        builder.Property(s => s.OverrideAmount).HasPrecision(12, 2);
        builder.Property(s => s.OverrideReason).HasMaxLength(500);
        builder.Property(s => s.Explanation).HasMaxLength(4000);
        builder.Property(s => s.CoverageStatus).HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.LinesTotal).HasPrecision(12, 2);
        builder.Property(s => s.ConfirmedByName).HasMaxLength(200);
        builder.Property(s => s.ConfirmedWithUnpricedGoodsReason).HasMaxLength(500);
        builder.HasIndex(s => new { s.TenantId, s.TransportOrderId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(s => s.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class TransportOrderServiceLineConfiguration : IEntityTypeConfiguration<TransportOrderServiceLine>
{
    public void Configure(EntityTypeBuilder<TransportOrderServiceLine> builder)
    {
        builder.ToTable("order_service_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.NameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Value).HasPrecision(12, 2);
        builder.Property(l => l.Amount).HasPrecision(12, 2);
        builder.Property(l => l.Quantity).HasPrecision(12, 3);
        builder.Property(l => l.PalletCount).HasPrecision(12, 3);
        builder.Property(l => l.DayCount).HasPrecision(12, 3);
        builder.Property(l => l.InvoiceDescriptionSnapshot).HasMaxLength(300);
        builder.HasIndex(l => new { l.TenantId, l.TransportOrderId });
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(l => l.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class TransportOrderDocumentConfiguration : IEntityTypeConfiguration<TransportOrderDocument>
{
    public void Configure(EntityTypeBuilder<TransportOrderDocument> builder)
    {
        // D6: a document hangs on an order, on a dossier, or on an order INSIDE a dossier — never on nothing.
        builder.ToTable("order_documents", table =>
            table.HasCheckConstraint("CK_order_documents_order_or_dossier",
                "\"TransportOrderId\" IS NOT NULL OR \"DossierId\" IS NOT NULL"));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.CustomTypeName).HasMaxLength(150);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(200);
        builder.Property(d => d.DocumentPath).HasMaxLength(500);
        builder.Property(d => d.FileName).HasMaxLength(300);
        builder.Property(d => d.ContentType).HasMaxLength(150);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        // H-14: internal until explicitly published to the customer portal.
        builder.Property(d => d.CustomerVisible).HasDefaultValue(false);
        builder.HasIndex(d => new { d.TenantId, d.TransportOrderId });
        builder.HasIndex(d => new { d.TenantId, d.DossierId });
        // Optional since D6 (null = dossier document); an order document still dies with its order.
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(d => d.TransportOrderId)
            .IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        // Restrict: a dossier is soft-deleted, so the FK never fires — and a hard delete must never
        // silently take documents (and their stored files) with it.
        builder.HasOne<Modules.Dossiers.Entities.TransportDossier>().WithMany().HasForeignKey(d => d.DossierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class IssuedTransportDocumentConfiguration : IEntityTypeConfiguration<IssuedTransportDocument>
{
    public void Configure(EntityTypeBuilder<IssuedTransportDocument> builder)
    {
        builder.ToTable("issued_transport_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.DocumentNumber).IsRequired().HasMaxLength(40);
        builder.Property(d => d.ExternalNumber).HasMaxLength(60);

        // The database is the final arbiter: a number exists once per tenant, EVER (unfiltered —
        // a soft-deleted document never releases its number), and one request issues one document.
        builder.HasIndex(d => new { d.TenantId, d.DocumentNumber }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.RequestId }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.TransportOrderId });
        builder.HasIndex(d => new { d.TenantId, d.DossierId });

        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(d => d.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Modules.Dossiers.Entities.TransportDossier>().WithMany().HasForeignKey(d => d.DossierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class TransportDocumentSequenceConfiguration : IEntityTypeConfiguration<TransportDocumentSequence>
{
    public void Configure(EntityTypeBuilder<TransportDocumentSequence> builder)
    {
        builder.ToTable("transport_document_sequences");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);

        // Concurrency token: two concurrent claims of the same value conflict at SaveChanges.
        builder.Property(s => s.NextValue).IsConcurrencyToken();

        builder.HasIndex(s => new { s.TenantId, s.Kind, s.Year })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class TenantDocumentRuleConfiguration : IEntityTypeConfiguration<TenantDocumentRule>
{
    public void Configure(EntityTypeBuilder<TenantDocumentRule> builder)
    {
        builder.ToTable("tenant_document_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.DocumentKind).IsRequired().HasMaxLength(20);
        builder.HasIndex(r => new { r.TenantId, r.Priority });
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class OrderPriceLineCargoLinkConfiguration : IEntityTypeConfiguration<OrderPriceLineCargoLink>
{
    public void Configure(EntityTypeBuilder<OrderPriceLineCargoLink> builder)
    {
        builder.ToTable("order_price_line_cargo_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.LineKey).IsRequired().HasMaxLength(200);

        // One link per (sales line, goods line). Keyed on LineKey, not the line's row id: Auto
        // lines are rewritten on every recalculation while their LineKey survives.
        builder.HasIndex(l => new { l.TenantId, l.TransportOrderId, l.LineKey, l.CargoItemId }).IsUnique();
        builder.HasIndex(l => l.CargoItemId);

        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(l => l.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CargoItem>().WithMany().HasForeignKey(l => l.CargoItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
