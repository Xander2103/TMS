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
        builder.Property(s => s.DriverNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.OrderNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.TripNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.InvoiceNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.VehicleNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.TrailerNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.CompanyLegalName).HasMaxLength(200);
        builder.Property(s => s.TradingName).HasMaxLength(200);
        builder.Property(s => s.CompanyNumber).HasMaxLength(50);
        builder.Property(s => s.VatNumber).HasMaxLength(30);
        builder.Property(s => s.Street).HasMaxLength(150);
        builder.Property(s => s.HouseNumber).HasMaxLength(20);
        builder.Property(s => s.PostalCode).HasMaxLength(20);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.CountryCode).HasMaxLength(2);
        builder.Property(s => s.OperationalStreet).HasMaxLength(150);
        builder.Property(s => s.OperationalHouseNumber).HasMaxLength(20);
        builder.Property(s => s.OperationalPostalCode).HasMaxLength(20);
        builder.Property(s => s.OperationalCity).HasMaxLength(100);
        builder.Property(s => s.OperationalCountryCode).HasMaxLength(2);
        builder.Property(s => s.Email).HasMaxLength(250);
        builder.Property(s => s.PhoneNumber).HasMaxLength(30);
        builder.Property(s => s.Website).HasMaxLength(200);
        builder.Property(s => s.Iban).HasMaxLength(34);
        builder.Property(s => s.DefaultCurrency).IsRequired().HasMaxLength(3).HasDefaultValue("EUR");
        builder.Property(s => s.InvoiceEmail).HasMaxLength(250);
        builder.Property(s => s.PaymentTermDays).HasDefaultValue(30);
        builder.Property(s => s.DefaultVatRatePercent).HasPrecision(5, 2).HasDefaultValue(21m);
        builder.Property(s => s.DefaultLoadingMinutes).HasDefaultValue(30);
        builder.Property(s => s.DefaultUnloadingMinutes).HasDefaultValue(30);
        builder.Property(s => s.TrainingConflictSeverity).IsRequired().HasMaxLength(16).HasDefaultValue("Warning");
        builder.Property(s => s.ShiftOverlapConflictSeverity).IsRequired().HasMaxLength(16).HasDefaultValue("Warning");
        builder.Property(s => s.DateFormat).IsRequired().HasMaxLength(20).HasDefaultValue("dd-MM-yyyy");
        builder.Property(s => s.DecimalSeparator).IsRequired().HasMaxLength(1).HasDefaultValue(",");
        builder.Property(s => s.DefaultWeightUnit).IsRequired().HasMaxLength(10).HasDefaultValue("kg");
        builder.Property(s => s.DefaultDistanceUnit).IsRequired().HasMaxLength(10).HasDefaultValue("km");
        builder.Property(s => s.OrderNumberNextValue).HasDefaultValue(1);
        builder.Property(s => s.TripNumberNextValue).HasDefaultValue(1);
        builder.Property(s => s.InvoiceNumberNextValue).HasDefaultValue(1);
        builder.Property(s => s.VehicleNumberNextValue).HasDefaultValue(1);
        builder.Property(s => s.TrailerNumberNextValue).HasDefaultValue(1);
        builder.Property(s => s.PackageNumberPrefix).HasMaxLength(20).HasDefaultValue("PKG-");
        builder.Property(s => s.PackageNumberNextValue).HasDefaultValue(1);
        builder.Property(s => s.LogoReference).HasMaxLength(300);
        builder.Property(s => s.EnabledModulesJson).IsRequired();

        // Numbering counters are optimistic-concurrency tokens: two requests claiming the same
        // next value conflict at SaveChanges instead of silently producing duplicate numbers.
        // Services retry via TenantNumbering.SaveWithClaimedNumberAsync (reload + re-claim).
        builder.Property(s => s.EmployeeNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.CustomerNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.DriverNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.VehicleNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.TrailerNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.OrderNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.TripNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.PackageNumberNextValue).IsConcurrencyToken();
        builder.Property(s => s.InvoiceNumberNextValue).IsConcurrencyToken();
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithOne().HasForeignKey<TenantSettings>(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
