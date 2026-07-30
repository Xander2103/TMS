using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Modules.Organization.Configurations;

public class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LegalName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.TradingName).HasMaxLength(200);
        builder.Property(e => e.CompanyNumber).HasMaxLength(30);
        builder.Property(e => e.VatNumber).HasMaxLength(30);
        builder.Property(e => e.PeppolId).HasMaxLength(64);
        builder.Property(e => e.PeppolScheme).HasMaxLength(10);
        builder.Property(e => e.Street).HasMaxLength(200);
        builder.Property(e => e.HouseNumber).HasMaxLength(20);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.CountryCode).HasMaxLength(2);
        builder.Property(e => e.Email).HasMaxLength(250);
        builder.Property(e => e.PhoneNumber).HasMaxLength(30);
        builder.Property(e => e.Website).HasMaxLength(250);
        builder.Property(e => e.Iban).HasMaxLength(34);
        builder.Property(e => e.Bic).HasMaxLength(11);
        builder.Property(e => e.BankName).HasMaxLength(100);
        builder.Property(e => e.DefaultCurrency).IsRequired().HasMaxLength(3);
        builder.Property(e => e.InvoiceNumberFormat).IsRequired().HasMaxLength(50);
        builder.Property(e => e.InvoicePrefix).HasMaxLength(20);
        builder.Property(e => e.CreditNotePrefix).HasMaxLength(20);
        builder.Property(e => e.InvoiceFooter).HasMaxLength(2000);
        builder.Property(e => e.LogoStorageKey).HasMaxLength(500);
        builder.Property(e => e.LogoFileName).HasMaxLength(255);
        builder.Property(e => e.LogoContentType).HasMaxLength(100);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.IsActive });
        // At most one default entity per tenant (soft-deleted rows excluded).
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("ix_legal_entities_tenant_default")
            .IsUnique()
            .HasFilter("\"IsDefault\" = true AND \"IsDeleted\" = false");

        // A Peppol participant identity is GLOBALLY unique across tenants: inbound documents are
        // routed to a tenant by this pair, so allowing tenant B to claim tenant A's identity would
        // misroute A's supplier invoices into B. The index is deliberately not tenant-scoped.
        builder.HasIndex(e => new { e.PeppolScheme, e.PeppolId })
            .HasDatabaseName("ix_legal_entities_peppol_participant")
            .IsUnique()
            .HasFilter("\"PeppolScheme\" IS NOT NULL AND \"PeppolId\" IS NOT NULL AND \"IsActive\" = true AND \"IsDeleted\" = false");

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class UserLegalEntitySelectionConfiguration : IEntityTypeConfiguration<UserLegalEntitySelection>
{
    public void Configure(EntityTypeBuilder<UserLegalEntitySelection> builder)
    {
        builder.ToTable("user_legal_entity_selections");
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.TenantId, s.UserId }).IsUnique();

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(s => s.LegalEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
