using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Configurations;

public class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("rate_cards");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.BaseAmount).HasPrecision(12, 2);
        builder.Property(r => r.PerKmRate).HasPrecision(12, 4);
        builder.Property(r => r.PerPalletRate).HasPrecision(12, 2);
        builder.Property(r => r.PerTonRate).HasPrecision(12, 2);
        builder.Property(r => r.MinimumAmount).HasPrecision(12, 2);
        builder.Property(r => r.Notes).HasMaxLength(2000);

        builder.HasMany(r => r.Surcharges).WithOne().HasForeignKey(s => s.RateCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.TenantId, r.CustomerId, r.EffectiveFrom });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class RateSurchargeConfiguration : IEntityTypeConfiguration<RateSurcharge>
{
    public void Configure(EntityTypeBuilder<RateSurcharge> builder)
    {
        builder.ToTable("rate_surcharges");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Value).HasPrecision(12, 4);

        builder.HasIndex(s => new { s.TenantId, s.RateCardId });

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
