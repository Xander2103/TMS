using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Eligibility.Entities;

namespace TransportationService.Api.Modules.Eligibility.Configurations;

public class EligibilityOverrideConfiguration : IEntityTypeConfiguration<EligibilityOverride>
{
    public void Configure(EntityTypeBuilder<EligibilityOverride> builder)
    {
        builder.ToTable("eligibility_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.RelatedEntityType).HasMaxLength(100);
        builder.Property(o => o.RuleCode).HasMaxLength(100);
        builder.Property(o => o.Reason).IsRequired().HasMaxLength(2000);
        builder.HasIndex(o => new { o.TenantId, o.EmployeeId });
    }
}
