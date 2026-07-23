using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Configurations;

public class AbsenceConfiguration : IEntityTypeConfiguration<Absence>
{
    public void Configure(EntityTypeBuilder<Absence> builder)
    {
        builder.ToTable("absences", table =>
            table.HasCheckConstraint("CK_absences_period", "\"EndDate\" >= \"StartDate\""));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.PartDay).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.Property(a => a.DecisionNote).HasMaxLength(1000);
        builder.Property(a => a.InternalNote).HasMaxLength(2000);
        builder.Property(a => a.AttachmentPath).HasMaxLength(300);
        builder.Property(a => a.AttachmentFileName).HasMaxLength(255);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.StartDate });
        builder.HasIndex(a => new { a.TenantId, a.StartDate });
        builder.HasIndex(a => new { a.TenantId, a.LeaveTypeId });

        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LeaveType>().WithMany().HasForeignKey(a => a.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
