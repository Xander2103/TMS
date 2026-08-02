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
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.LinesTotal).HasPrecision(12, 2);
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
        builder.ToTable("order_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.CustomTypeName).HasMaxLength(150);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(200);
        builder.Property(d => d.DocumentPath).HasMaxLength(500);
        builder.Property(d => d.FileName).HasMaxLength(300);
        builder.Property(d => d.ContentType).HasMaxLength(150);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.HasIndex(d => new { d.TenantId, d.TransportOrderId });
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(d => d.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
