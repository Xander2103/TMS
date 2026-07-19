using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Configurations;

public class EmployeeJobFunctionConfiguration : IEntityTypeConfiguration<EmployeeJobFunction>
{
    public void Configure(EntityTypeBuilder<EmployeeJobFunction> builder)
    {
        builder.ToTable("employee_job_functions");
        builder.HasKey(j => new { j.EmployeeId, j.JobFunctionId });

        builder.HasOne(j => j.JobFunction)
            .WithMany()
            .HasForeignKey(j => j.JobFunctionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(j => j.JobFunctionId);
    }
}
