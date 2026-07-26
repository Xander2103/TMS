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
        builder.Property(r => r.MaximumAmount).HasPrecision(12, 2);
        builder.Property(r => r.BracketMode).HasConversion<string>().HasMaxLength(20).HasDefaultValue(BracketSelectionMode.Absolute);
        builder.Property(r => r.BaseAmount).HasPrecision(12, 2);
        builder.Property(r => r.MinimumQuantity).HasPrecision(12, 3);
        builder.Property(r => r.QuantityRoundingStep).HasPrecision(8, 3);
        builder.Property(r => r.OversizeLengthCm).HasPrecision(10, 2);
        builder.Property(r => r.OversizeWidthCm).HasPrecision(10, 2);
        builder.Property(r => r.OversizeBillableFactor).HasPrecision(8, 2);
        builder.HasMany(r => r.Brackets).WithOne().HasForeignKey(b => b.PriceRuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Agreement).WithMany().HasForeignKey(r => r.AgreementId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(r => new { r.TenantId, r.CustomerId, r.UnitTypeId, r.EffectiveFrom });
        builder.HasIndex(r => new { r.TenantId, r.AgreementId });
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class PricingAgreementConfiguration : IEntityTypeConfiguration<PricingAgreement>
{
    public void Configure(EntityTypeBuilder<PricingAgreement> builder)
    {
        builder.ToTable("pricing_agreements");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Currency).HasMaxLength(3);
        builder.Property(a => a.MinimumAmount).HasPrecision(12, 2);
        builder.Property(a => a.MaximumAmount).HasPrecision(12, 2);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.HasMany(a => a.Surcharges).WithOne().HasForeignKey(s => s.AgreementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Assignments).WithOne(x => x.Agreement).HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Modifiers).WithOne().HasForeignKey(m => m.AgreementId).OnDelete(DeleteBehavior.Cascade);
        // Self-referencing derivation FK: Restrict, never Cascade — deleting a base table that
        // other tables derive from must be blocked (enforced in PricingAdminService), not silently
        // cascade-delete the derived tables.
        builder.HasOne(a => a.BaseAgreement).WithMany().HasForeignKey(a => a.BaseAgreementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TenantId, a.CustomerId, a.EffectiveFrom });
        builder.HasIndex(a => new { a.TenantId, a.LegacyRateCardId });
        builder.HasIndex(a => new { a.TenantId, a.BaseAgreementId });
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class PricingAgreementModifierConfiguration : IEntityTypeConfiguration<PricingAgreementModifier>
{
    public void Configure(EntityTypeBuilder<PricingAgreementModifier> builder)
    {
        builder.ToTable("pricing_agreement_modifiers");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.CountryCode).HasMaxLength(2);
        builder.Property(m => m.Percent).HasPrecision(7, 3);
        builder.Property(m => m.FixedAmount).HasPrecision(12, 2);
        builder.HasIndex(m => new { m.TenantId, m.AgreementId });
        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class PricingAgreementAssignmentConfiguration : IEntityTypeConfiguration<PricingAgreementAssignment>
{
    public void Configure(EntityTypeBuilder<PricingAgreementAssignment> builder)
    {
        builder.ToTable("pricing_agreement_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PercentAdjustment).HasPrecision(7, 3);
        builder.Property(x => x.FixedAdjustment).HasPrecision(12, 2);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.TenantId, x.AgreementId });
        builder.HasIndex(x => new { x.TenantId, x.CustomerId });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public class PricingAgreementSurchargeConfiguration : IEntityTypeConfiguration<PricingAgreementSurcharge>
{
    public void Configure(EntityTypeBuilder<PricingAgreementSurcharge> builder)
    {
        builder.ToTable("pricing_agreement_surcharges");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Value).HasPrecision(12, 2);
        builder.HasIndex(s => new { s.TenantId, s.AgreementId });
        builder.HasQueryFilter(s => !s.IsDeleted);
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
        builder.Property(b => b.WeightToKg).HasPrecision(10, 2);
        builder.Property(b => b.VolumeToM3).HasPrecision(10, 2);
        builder.Property(b => b.LoadingMetersTo).HasPrecision(10, 2);
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
        builder.Property(o => o.Description).HasMaxLength(1000);
        builder.Property(o => o.InvoiceDescription).HasMaxLength(300);
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
        builder.Property(p => p.MinimumAmount).HasPrecision(12, 2);
        builder.Property(p => p.InvoiceDescription).HasMaxLength(300);
        builder.HasIndex(p => new { p.TenantId, p.CustomerId, p.ServiceOptionId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasOne<ServiceOption>().WithMany().HasForeignKey(p => p.ServiceOptionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class ScheduledPriceAdjustmentConfiguration : IEntityTypeConfiguration<ScheduledPriceAdjustment>
{
    public void Configure(EntityTypeBuilder<ScheduledPriceAdjustment> builder)
    {
        builder.ToTable("scheduled_price_adjustments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Percent).HasPrecision(7, 3);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.HasMany(a => a.Rules).WithOne().HasForeignKey(r => r.AdjustmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.TenantId, a.CustomerId, a.EffectiveDate });
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class ScheduledPriceAdjustmentRuleConfiguration : IEntityTypeConfiguration<ScheduledPriceAdjustmentRule>
{
    public void Configure(EntityTypeBuilder<ScheduledPriceAdjustmentRule> builder)
    {
        builder.ToTable("scheduled_price_adjustment_rules");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.TenantId, r.AdjustmentId });
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class CustomerPreferredUnitConfiguration : IEntityTypeConfiguration<CustomerPreferredUnit>
{
    public void Configure(EntityTypeBuilder<CustomerPreferredUnit> builder)
    {
        builder.ToTable("customer_preferred_units");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.CustomerLabel).HasMaxLength(150);
        builder.Property(u => u.EdiCode).HasMaxLength(50);
        builder.Property(u => u.ExcelCode).HasMaxLength(50);
        builder.HasIndex(u => new { u.TenantId, u.CustomerId, u.UnitTypeId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
