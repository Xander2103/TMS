using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeNumber).IsRequired().HasMaxLength(30);
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Street).IsRequired().HasMaxLength(150);
        builder.Property(e => e.HouseNumber).IsRequired().HasMaxLength(20);
        builder.Property(e => e.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.City).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Country).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(250);
        builder.Property(e => e.EmploymentStatus).HasConversion<string>();
        builder.Property(e => e.PrimaryFunction).HasConversion<string>();
        builder.HasIndex(e => new { e.TenantId, e.EmployeeNumber }).IsUnique();
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.IsActive });
    }
}
