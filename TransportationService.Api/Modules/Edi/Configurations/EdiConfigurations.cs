using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Edi.Configurations;

public class TradingPartnerConfiguration : IEntityTypeConfiguration<TradingPartner>
{
    public void Configure(EntityTypeBuilder<TradingPartner> builder)
    {
        builder.ToTable("trading_partners");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.ExternalCustomerIdentifier).HasMaxLength(100);
        builder.Property(p => p.MappingProfile).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => new { p.TenantId, p.Code })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Customer>().WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class EdiPartnerLocationConfiguration : IEntityTypeConfiguration<EdiPartnerLocation>
{
    public void Configure(EntityTypeBuilder<EdiPartnerLocation> builder)
    {
        builder.ToTable("edi_partner_locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ExternalLocationCode).IsRequired().HasMaxLength(100);

        builder.HasIndex(l => new { l.TradingPartnerId, l.ExternalLocationCode })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<TradingPartner>().WithMany().HasForeignKey(l => l.TradingPartnerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Location>().WithMany().HasForeignKey(l => l.LocationId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}

public class EdiMessageConfiguration : IEntityTypeConfiguration<EdiMessage>
{
    public void Configure(EntityTypeBuilder<EdiMessage> builder)
    {
        builder.ToTable("edi_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.MessageType).IsRequired().HasMaxLength(50);
        builder.Property(m => m.ExternalReference).HasMaxLength(100);
        builder.Property(m => m.PayloadJson).IsRequired();
        builder.Property(m => m.PayloadHash).IsRequired().HasMaxLength(64);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ErrorDetail).HasMaxLength(2000);
        builder.Property(m => m.ValidationErrorsJson).HasMaxLength(4000);
        builder.Property(m => m.ResultEntityType).HasMaxLength(100);
        builder.Property(m => m.ResultEntityId).HasMaxLength(100);

        builder.HasIndex(m => new { m.TenantId, m.Status });
        builder.HasIndex(m => new { m.TenantId, m.TradingPartnerId, m.ExternalReference });

        builder.HasOne<TradingPartner>().WithMany().HasForeignKey(m => m.TradingPartnerId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
