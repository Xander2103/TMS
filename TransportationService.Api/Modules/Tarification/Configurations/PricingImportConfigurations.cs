using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Configurations;

public class PricingImportProfileConfiguration : IEntityTypeConfiguration<PricingImportProfile>
{
    public void Configure(EntityTypeBuilder<PricingImportProfile> builder)
    {
        builder.ToTable("pricing_import_profiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(120);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.MappingJson).IsRequired();
        builder.Property(p => p.SheetName).HasMaxLength(120);

        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => new { p.TenantId, p.Name }).IsUnique().HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class PricingImportRunConfiguration : IEntityTypeConfiguration<PricingImportRun>
{
    public void Configure(EntityTypeBuilder<PricingImportRun> builder)
    {
        builder.ToTable("pricing_import_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FileName).IsRequired().HasMaxLength(260);
        builder.Property(r => r.Checksum).IsRequired().HasMaxLength(64);
        builder.Property(r => r.ProfileName).HasMaxLength(120);
        builder.Property(r => r.Mode).IsRequired().HasMaxLength(40);

        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => new { r.TenantId, r.AgreementId });
        // Recognising "this exact file again" is an indexed lookup, not a scan.
        builder.HasIndex(r => new { r.TenantId, r.Checksum });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
