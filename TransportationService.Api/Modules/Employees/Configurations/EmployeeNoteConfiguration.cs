using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Configurations;

public class EmployeeNoteConfiguration : IEntityTypeConfiguration<EmployeeNote>
{
    public void Configure(EntityTypeBuilder<EmployeeNote> builder)
    {
        builder.ToTable("employee_notes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Text).IsRequired().HasMaxLength(4000);

        builder.HasIndex(n => new { n.TenantId, n.EmployeeId });
        builder.HasIndex(n => new { n.TenantId, n.IsPinnedToDashboard })
            .HasFilter("\"IsDeleted\" = false AND \"IsPinnedToDashboard\" = true");

        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(n => n.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(n => !n.IsDeleted);
    }
}
