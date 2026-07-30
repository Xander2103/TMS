using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Peppol.Entities;

namespace TransportationService.Api.Modules.Peppol.Configurations;

public class PeppolSettingsConfiguration : IEntityTypeConfiguration<PeppolSettings>
{
    public void Configure(EntityTypeBuilder<PeppolSettings> builder)
    {
        builder.ToTable("peppol_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Environment).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ProviderKey).IsRequired().HasMaxLength(50);
        builder.Property(s => s.InvoiceEmailFallback).HasMaxLength(250);
        builder.Property(s => s.DefaultInvoiceNote).HasMaxLength(1000);

        // One settings row per legal entity per tenant. Plain unique index (no soft-delete
        // filter) — this is single-row live configuration, not history that gets re-created.
        builder.HasIndex(s => new { s.TenantId, s.LegalEntityId }).IsUnique();

        builder.HasOne<Modules.Organization.Entities.LegalEntity>().WithMany()
            .HasForeignKey(s => s.LegalEntityId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class PeppolTransmissionConfiguration : IEntityTypeConfiguration<PeppolTransmission>
{
    public void Configure(EntityTypeBuilder<PeppolTransmission> builder)
    {
        builder.ToTable("peppol_transmissions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.DocumentKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Environment).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ProviderKey).IsRequired().HasMaxLength(50);
        builder.Property(t => t.ProviderMessageId).HasMaxLength(100);
        builder.Property(t => t.SellerParticipant).IsRequired().HasMaxLength(80);
        builder.Property(t => t.BuyerParticipant).IsRequired().HasMaxLength(80);
        builder.Property(t => t.PayloadStorageKey).HasMaxLength(300);
        builder.Property(t => t.PayloadHash).HasMaxLength(64);
        builder.Property(t => t.ErrorDetail).HasMaxLength(2000);
        builder.Property(t => t.ResponseCode).HasMaxLength(50);

        // Named explicitly so this plain lookup index stays DISTINCT from the filtered unique
        // index below — a second HasIndex over the same properties without its own name would
        // silently reconfigure the first instead of adding one.
        builder.HasIndex(t => new { t.TenantId, t.InvoiceId }, "IX_peppol_transmissions_tenant_invoice");
        builder.HasIndex(t => new { t.TenantId, t.Status }).HasDatabaseName("IX_peppol_transmissions_tenant_status");

        // At most one NON-TERMINAL transmission per invoice. Partial unique index — the filter
        // syntax is valid on both PostgreSQL and the SQLite test harness, so tests exercise the
        // real constraint. The phase-13 queue service adds a friendly Dutch duplicate check in
        // front of it; this index is the concurrency-safe backstop.
        builder.HasIndex(t => new { t.TenantId, t.InvoiceId }, "IX_peppol_transmissions_one_active_per_invoice")
            .IsUnique()
            .HasFilter("\"Status\" NOT IN ('Failed','Rejected','Cancelled')");

        builder.HasOne<Modules.Invoicing.Entities.Invoice>().WithMany()
            .HasForeignKey(t => t.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Events).WithOne().HasForeignKey(e => e.TransmissionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class PeppolTransmissionEventConfiguration : IEntityTypeConfiguration<PeppolTransmissionEvent>
{
    public void Configure(EntityTypeBuilder<PeppolTransmissionEvent> builder)
    {
        builder.ToTable("peppol_transmission_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Detail).HasMaxLength(2000);

        builder.HasIndex(e => new { e.TenantId, e.TransmissionId });

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class PeppolIncomingDocumentConfiguration : IEntityTypeConfiguration<PeppolIncomingDocument>
{
    public void Configure(EntityTypeBuilder<PeppolIncomingDocument> builder)
    {
        builder.ToTable("peppol_incoming_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocumentKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.SupplierParticipant).IsRequired().HasMaxLength(80);
        builder.Property(d => d.SupplierName).HasMaxLength(200);
        builder.Property(d => d.DocumentNumber).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Amount).HasPrecision(18, 2);
        builder.Property(d => d.Currency).HasMaxLength(3);
        builder.Property(d => d.PayloadStorageKey).HasMaxLength(300);
        builder.Property(d => d.PayloadHash).IsRequired().HasMaxLength(64);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ProviderMessageId).IsRequired().HasMaxLength(100);
        builder.Property(d => d.ReviewNote).HasMaxLength(2000);

        builder.HasIndex(d => new { d.TenantId, d.ProviderMessageId }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.Status });

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
