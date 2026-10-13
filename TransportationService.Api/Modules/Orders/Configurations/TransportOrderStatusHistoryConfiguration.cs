using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Configurations;

public class TransportOrderStatusHistoryConfiguration : IEntityTypeConfiguration<TransportOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<TransportOrderStatusHistory> builder)
    {
        builder.ToTable("transport_order_status_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Reason).HasMaxLength(1000);

        builder.HasIndex(h => new { h.TenantId, h.TransportOrderId, h.ChangedAt });

        builder.HasOne<TransportOrder>()
            .WithMany()
            .HasForeignKey(h => h.TransportOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
