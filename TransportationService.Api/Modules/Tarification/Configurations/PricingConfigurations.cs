using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Configurations;

public class PricingZoneConfiguration : IEntityTypeConfiguration<PricingZone>
{
    public void Configure(EntityTypeBuilder<PricingZone> builder)
    {
        builder.ToTable("pricing_zones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Code).IsRequired().HasMaxLength(30);
        builder.Property(z => z.Name).IsRequired().HasMaxLength(150);
        builder.HasIndex(z => new { z.TenantId, z.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasMany(z => z.Areas).WithOne().HasForeignKey(a => a.ZoneId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(z => !z.IsDeleted);
    }
}

public class PricingZoneAreaConfiguration : IEntityTypeConfiguration<PricingZoneArea>
{
    public void Configure(EntityTypeBuilder<PricingZoneArea> builder)
    {
        builder.ToTable("pricing_zone_areas");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CountryCode).IsRequired().HasMaxLength(2);
        builder.Property(a => a.PostalCodeFrom).IsRequired().HasMaxLength(20);
        builder.Property(a => a.PostalCodeTo).IsRequired().HasMaxLength(20);
        builder.HasIndex(a => new { a.TenantId, a.ZoneId });
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.ToTable("price_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.Basis).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.UnitPrice).HasPrecision(12, 4);
        builder.Property(r => r.MinimumAmount).HasPrecision(12, 2);
        builder.HasMany(r => r.Brackets).WithOne().HasForeignKey(b => b.PriceRuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.TenantId, r.CustomerId, r.UnitTypeId, r.EffectiveFrom });
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class PriceRuleBracketConfiguration : IEntityTypeConfiguration<PriceRuleBracket>
{
    public void Configure(EntityTypeBuilder<PriceRuleBracket> builder)
    {
        builder.ToTable("price_rule_brackets");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.FromQuantity).HasPrecision(12, 3);
        builder.Property(b => b.ToQuantity).HasPrecision(12, 3);
        builder.Property(b => b.Price).HasPrecision(12, 2);
        builder.Property(b => b.PricePerExtraUnit).HasPrecision(12, 4);
        builder.HasIndex(b => new { b.TenantId, b.PriceRuleId });
        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}

public class ServiceOptionConfiguration : IEntityTypeConfiguration<ServiceOption>
{
    public void Configure(EntityTypeBuilder<ServiceOption> builder)
    {
        builder.ToTable("service_options");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Code).IsRequired().HasMaxLength(50);
        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.DefaultValue).HasPrecision(12, 2);
        builder.HasIndex(o => new { o.TenantId, o.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}

public class CustomerServiceOptionPriceConfiguration : IEntityTypeConfiguration<CustomerServiceOptionPrice>
{
    public void Configure(EntityTypeBuilder<CustomerServiceOptionPrice> builder)
    {
        builder.ToTable("customer_service_option_prices");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Value).HasPrecision(12, 2);
        builder.HasIndex(p => new { p.TenantId, p.CustomerId, p.ServiceOptionId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasOne<ServiceOption>().WithMany().HasForeignKey(p => p.ServiceOptionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class CustomerPreferredUnitConfiguration : IEntityTypeConfiguration<CustomerPreferredUnit>
{
    public void Configure(EntityTypeBuilder<CustomerPreferredUnit> builder)
    {
        builder.ToTable("customer_preferred_units");
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => new { u.TenantId, u.CustomerId, u.UnitTypeId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
