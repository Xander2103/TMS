using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Modules.Tenancy.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("tenant_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Timezone).IsRequired().HasMaxLength(100);
        builder.Property(s => s.DefaultLanguage).IsRequired().HasMaxLength(10);
        builder.Property(s => s.EmployeeNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.CustomerNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.CompanyLegalName).HasMaxLength(200);
        builder.Property(s => s.VatNumber).HasMaxLength(30);
        builder.Property(s => s.Street).HasMaxLength(150);
        builder.Property(s => s.HouseNumber).HasMaxLength(20);
        builder.Property(s => s.PostalCode).HasMaxLength(20);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.CountryCode).HasMaxLength(2);
        builder.Property(s => s.Email).HasMaxLength(250);
        builder.Property(s => s.PhoneNumber).HasMaxLength(30);
        builder.Property(s => s.Website).HasMaxLength(200);
        builder.Property(s => s.Iban).HasMaxLength(34);
        builder.Property(s => s.DefaultCurrency).IsRequired().HasMaxLength(3);
        builder.Property(s => s.EnabledModulesJson).IsRequired();
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithOne().HasForeignKey<TenantSettings>(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
