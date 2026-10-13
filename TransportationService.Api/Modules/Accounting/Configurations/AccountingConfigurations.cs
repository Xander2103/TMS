using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Accounting.Entities;

namespace TransportationService.Api.Modules.Accounting.Configurations;

public class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("ledger_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AccountNumber).IsRequired().HasMaxLength(30);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.ExternalCode).HasMaxLength(50);
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.HasIndex(a => new { a.TenantId, a.AccountNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class SalesCategoryConfiguration : IEntityTypeConfiguration<SalesCategory>
{
    public void Configure(EntityTypeBuilder<SalesCategory> builder)
    {
        builder.ToTable("sales_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.SystemRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.InvoiceDescriptionNl).HasMaxLength(300);
        builder.Property(c => c.DefaultUnitCode).HasMaxLength(20);
        builder.Property(c => c.VatCategoryOverride).HasMaxLength(5);
        // Audit fix: bounded like InvoiceDescriptionNl; CostCentre must fit invoice_lines.CostCentreSnapshot (40).
        builder.Property(c => c.InvoiceDescriptionFr).HasMaxLength(300);
        builder.Property(c => c.InvoiceDescriptionEn).HasMaxLength(300);
        builder.Property(c => c.InvoiceDescriptionDe).HasMaxLength(300);
        builder.Property(c => c.CostCentre).HasMaxLength(40);
        builder.Property(c => c.DefaultPricingBasis).HasMaxLength(40);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.HasOne(c => c.LedgerAccount).WithMany().HasForeignKey(c => c.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(c => c.TenantId);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}

/// <summary>
/// Sprint 5G: per-invoicing-entity ledger mapping for a sales code, so one code can book to a
/// different account per legal entity without duplicating the code.
/// </summary>
public class SalesCategoryLedgerMappingConfiguration : IEntityTypeConfiguration<SalesCategoryLedgerMapping>
{
    public void Configure(EntityTypeBuilder<SalesCategoryLedgerMapping> builder)
    {
        builder.ToTable("sales_category_ledger_mappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.CostCentre).HasMaxLength(40);

        // One mapping per code per invoicing entity.
        builder.HasIndex(m => new { m.TenantId, m.SalesCategoryId, m.LegalEntityId })
            .IsUnique().HasFilter("\"IsDeleted\" = false");

        // Audit fix: real references, Restrict so a mapped account/entity can never vanish
        // underneath a sales code (the delete guard in AccountingService reports it first).
        builder.HasOne<LedgerAccount>().WithMany()
            .HasForeignKey(m => m.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Organization.Entities.LegalEntity>().WithMany()
            .HasForeignKey(m => m.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.LedgerAccountId);
        builder.HasIndex(m => m.LegalEntityId);

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
