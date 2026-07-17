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
        builder.Property(s => s.EnabledModulesJson).IsRequired();
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithOne().HasForeignKey<TenantSettings>(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
