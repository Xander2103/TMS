using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Modules.Qualifications.Configurations;

public class QualificationTypeConfiguration : IEntityTypeConfiguration<QualificationType>
{
    public void Configure(EntityTypeBuilder<QualificationType> builder)
    {
        builder.ToTable("qualification_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(50);
        builder.HasIndex(t => t.Code).IsUnique();
    }
}
