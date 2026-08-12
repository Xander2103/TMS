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
        builder.HasOne(c => c.LedgerAccount).WithMany().HasForeignKey(c => c.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(c => c.TenantId);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
