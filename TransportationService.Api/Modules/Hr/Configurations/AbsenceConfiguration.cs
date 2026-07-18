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
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.Property(a => a.DecisionNote).HasMaxLength(1000);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.StartDate });
        builder.HasIndex(a => new { a.TenantId, a.StartDate });

        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
