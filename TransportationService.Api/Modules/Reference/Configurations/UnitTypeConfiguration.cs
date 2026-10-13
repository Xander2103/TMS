using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Configurations;

public class UnitTypeConfiguration : LookupEntityTypeConfiguration<UnitType>
{
    protected override string TableName => "unit_types";

    public override void Configure(EntityTypeBuilder<UnitType> builder)
    {
        base.Configure(builder);

        builder.Property(u => u.Symbol).HasMaxLength(20);
        builder.Property(u => u.DefaultLengthCm).HasPrecision(10, 2);
        builder.Property(u => u.DefaultWidthCm).HasPrecision(10, 2);
        builder.Property(u => u.DefaultHeightCm).HasPrecision(10, 2);
        builder.Property(u => u.DefaultWeightKg).HasPrecision(12, 3);
        builder.Property(u => u.MaxWeightKg).HasPrecision(12, 3);
        builder.Property(u => u.DefaultVolumeM3).HasPrecision(12, 4);
        builder.Property(u => u.DefaultLoadingMeters).HasPrecision(8, 2);
        builder.Property(u => u.DefaultPalletPlaces).HasPrecision(8, 2);
    }
}
