using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Configurations;

public class LocationOpeningIntervalConfiguration : IEntityTypeConfiguration<LocationOpeningInterval>
{
    public void Configure(EntityTypeBuilder<LocationOpeningInterval> builder)
    {
        builder.ToTable("location_opening_intervals");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Note).HasMaxLength(200);

        builder.HasIndex(i => new { i.TenantId, i.LocationId });
    }
}
