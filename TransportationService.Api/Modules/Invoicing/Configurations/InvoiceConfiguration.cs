using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Invoicing.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(30);
        builder.Property(i => i.PurchaseOrderNumber).HasMaxLength(100);
        builder.Property(i => i.SellerName).HasMaxLength(200);
        builder.Property(i => i.SellerVatNumber).HasMaxLength(30);
        builder.Property(i => i.SellerIban).HasMaxLength(34);
        builder.Property(i => i.SellerAddressLine).HasMaxLength(300);
        builder.Property(i => i.CustomerVatTreatment).HasMaxLength(30);
        builder.Property(i => i.CustomerVatNumberSnapshot).HasMaxLength(30);
        builder.Property(i => i.VatLegalText).HasMaxLength(300);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Currency).IsRequired().HasMaxLength(3);
        builder.Property(i => i.Notes).HasMaxLength(4000);

        builder.HasIndex(i => new { i.TenantId, i.InvoiceNumber })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(i => new { i.TenantId, i.CustomerId });
        builder.HasIndex(i => new { i.TenantId, i.InvoiceDate });
        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.LegalEntityId, i.InvoicePeriodYear, i.InvoicePeriodMonth });

        builder.HasOne<Customer>().WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Organization.Entities.LegalEntity>().WithMany()
            .HasForeignKey(i => i.LegalEntityId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}

public class InvoiceAttachmentConfiguration : IEntityTypeConfiguration<InvoiceAttachment>
{
    public void Configure(EntityTypeBuilder<InvoiceAttachment> builder)
    {
        builder.ToTable("invoice_attachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Notes).HasMaxLength(500);

        builder.HasIndex(a => new { a.TenantId, a.InvoiceId });

        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("invoice_sequences");

        builder.HasKey(s => s.Id);

        // Concurrency token: two concurrent claims of the same value conflict at SaveChanges.
        builder.Property(s => s.NextValue).IsConcurrencyToken();

        builder.HasIndex(s => new { s.TenantId, s.LegalEntityId, s.Year, s.Month })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Modules.Organization.Entities.LegalEntity>().WithMany()
            .HasForeignKey(s => s.LegalEntityId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description).IsRequired().HasMaxLength(500);
        builder.Property(l => l.Quantity).HasPrecision(12, 2);
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.Property(l => l.VatRatePercent).HasPrecision(5, 2);

        builder.HasIndex(l => new { l.InvoiceId, l.Sequence });
        builder.HasIndex(l => new { l.TenantId, l.TransportOrderId });

        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(l => l.TransportOrderId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
