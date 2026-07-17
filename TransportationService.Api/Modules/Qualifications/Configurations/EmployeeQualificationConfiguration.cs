using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Modules.Qualifications.Configurations;

public class EmployeeQualificationConfiguration : IEntityTypeConfiguration<EmployeeQualification>
{
    public void Configure(EntityTypeBuilder<EmployeeQualification> builder)
    {
        builder.ToTable("employee_qualifications");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.DocumentNumber).HasMaxLength(100);
        builder.Property(q => q.DocumentPath).HasMaxLength(500);
        builder.Property(q => q.Notes).HasMaxLength(2000);
        builder.Property(q => q.Status).HasConversion<string>();
        builder.HasIndex(q => new { q.TenantId, q.EmployeeId });
        builder.HasIndex(q => new { q.TenantId, q.ExpiryDate });
        builder.HasOne<Employee>().WithMany().HasForeignKey(q => q.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QualificationType>().WithMany().HasForeignKey(q => q.QualificationTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
