using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Configurations;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("shifts");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.WorkLocation).HasMaxLength(200);
        builder.Property(s => s.RoleLabel).HasMaxLength(100);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.HasIndex(s => new { s.TenantId, s.Date });
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.Date });

        builder.HasOne<Employee>().WithMany().HasForeignKey(s => s.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
