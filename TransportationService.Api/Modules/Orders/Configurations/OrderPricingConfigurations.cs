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
        builder.HasIndex(l => new { l.TenantId, l.TransportOrderId });
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(l => l.TransportOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(l => !l.IsDeleted);
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
