using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Identity.Configurations;

public class UserSecurityTokenConfiguration : IEntityTypeConfiguration<UserSecurityToken>
{
    public void Configure(EntityTypeBuilder<UserSecurityToken> builder)
    {
        builder.ToTable("user_security_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);

        builder.HasIndex(t => t.TokenHash);
        builder.HasIndex(t => new { t.UserId, t.Kind });

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class JobFunctionRoleMappingConfiguration : IEntityTypeConfiguration<JobFunctionRoleMapping>
{
    public void Configure(EntityTypeBuilder<JobFunctionRoleMapping> builder)
    {
        builder.ToTable("job_function_role_mappings");
        builder.HasKey(m => m.Id);

        builder.HasIndex(m => new { m.TenantId, m.JobFunctionId, m.RoleId }).IsUnique();

        builder.HasOne<Role>().WithMany().HasForeignKey(m => m.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportationService.Api.Modules.Organization.Entities.JobFunction>()
            .WithMany().HasForeignKey(m => m.JobFunctionId).OnDelete(DeleteBehavior.Cascade);
    }
}
