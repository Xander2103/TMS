using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CustomerNumber).IsRequired().HasMaxLength(30);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.LegalName).HasMaxLength(200);
        builder.Property(c => c.Nickname).HasMaxLength(100);
        builder.Property(c => c.VatNumber).HasMaxLength(30);
        builder.Property(c => c.CompanyNumber).HasMaxLength(30);
        builder.Property(c => c.CurrencyCode).IsRequired().HasMaxLength(3).HasDefaultValue("EUR");
        builder.Property(c => c.Iban).HasMaxLength(34);
        builder.Property(c => c.Bic).HasMaxLength(11);
        builder.Property(c => c.BankName).HasMaxLength(100);
        builder.Property(c => c.BankAccountNumber).HasMaxLength(50);
        builder.Property(c => c.Email).HasMaxLength(250);
        builder.Property(c => c.PhoneNumber).HasMaxLength(30);
        builder.Property(c => c.Website).HasMaxLength(200);
        builder.Property(c => c.Street).HasMaxLength(150);
        builder.Property(c => c.HouseNumber).HasMaxLength(20);
        builder.Property(c => c.PostalCode).HasMaxLength(20);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.CountryCode).HasMaxLength(2);
        builder.Property(c => c.InvoiceEmail).HasMaxLength(250);
        builder.Property(c => c.DefaultLanguageCode).HasMaxLength(10);
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.Property(c => c.BlockReason).HasMaxLength(500);

        builder.Property(c => c.PurchaseOrderPolicy).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.VatTreatment).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.DefaultVatRatePercent).HasPrecision(5, 2);
        builder.Property(c => c.VatCountryCode).HasMaxLength(2);
        builder.Property(c => c.VatNotes).HasMaxLength(1000);
        builder.Property(c => c.PeppolId).HasMaxLength(64);
        builder.Property(c => c.PeppolScheme).HasMaxLength(10);
        builder.Property(c => c.InvoiceLanguageCode).HasMaxLength(10);

        builder.HasIndex(c => new { c.TenantId, c.CustomerNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => new { c.TenantId, c.IsActive });
        builder.HasIndex(c => new { c.TenantId, c.CategoryId });

        builder.HasOne<Modules.Organization.Entities.LegalEntity>().WithMany()
            .HasForeignKey(c => c.DefaultLegalEntityId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.SetNull);

        builder.HasMany(c => c.Contacts)
            .WithOne()
            .HasForeignKey(contact => contact.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
