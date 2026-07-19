using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Identity.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(150);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.TemplateCode).HasMaxLength(40);
        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
        builder.HasIndex(r => r.TenantId);
        // At most one role per tenant may carry a given template identity.
        builder.HasIndex(r => new { r.TenantId, r.TemplateCode })
            .IsUnique()
            .HasFilter("\"TemplateCode\" IS NOT NULL");
    }
}

public class RoleTemplateStateConfiguration : IEntityTypeConfiguration<RoleTemplateState>
{
    public void Configure(EntityTypeBuilder<RoleTemplateState> builder)
    {
        builder.ToTable("role_template_states");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
