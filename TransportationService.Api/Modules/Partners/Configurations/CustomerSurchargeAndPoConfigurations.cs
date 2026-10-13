using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class CustomerDieselSurchargeConfiguration : IEntityTypeConfiguration<CustomerDieselSurcharge>
{
    public void Configure(EntityTypeBuilder<CustomerDieselSurcharge> builder)
    {
        builder.ToTable("customer_diesel_surcharges");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Percent).HasPrecision(5, 2);
        builder.Property(s => s.Basis).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Presentation).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Rounding).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.FormulaDescription).HasMaxLength(1000);

        // One configuration per customer.
        builder.HasIndex(s => new { s.TenantId, s.CustomerId }).IsUnique().HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class CustomerPurchaseOrderNumberConfiguration : IEntityTypeConfiguration<CustomerPurchaseOrderNumber>
{
    public void Configure(EntityTypeBuilder<CustomerPurchaseOrderNumber> builder)
    {
        builder.ToTable("customer_purchase_order_numbers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PoNumber).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(500);

        builder.HasIndex(p => new { p.TenantId, p.CustomerId, p.ValidFrom });

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
