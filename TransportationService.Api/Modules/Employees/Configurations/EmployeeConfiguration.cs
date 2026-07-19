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
        builder.Property(e => e.PlaceOfBirth).HasMaxLength(100);
        builder.Property(e => e.NationalityCode).HasMaxLength(10);
        builder.Property(e => e.NationalRegisterNumber).HasMaxLength(15);
        builder.Property(e => e.PreferredLanguageCode).HasMaxLength(10);
        builder.Property(e => e.Street).IsRequired().HasMaxLength(150);
        builder.Property(e => e.HouseNumber).IsRequired().HasMaxLength(20);
        builder.Property(e => e.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.City).IsRequired().HasMaxLength(100);
        builder.Property(e => e.CountryCode).HasMaxLength(2);
        builder.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(30);
        builder.Property(e => e.MobilePhone).HasMaxLength(30);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(250);
        builder.Property(e => e.EmergencyContactName).HasMaxLength(150);
        builder.Property(e => e.EmergencyContactPhone).HasMaxLength(30);
        builder.Property(e => e.Iban).HasMaxLength(34);
        builder.Property(e => e.Bic).HasMaxLength(11);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.EmploymentStatus).HasConversion<string>();

        builder.HasIndex(e => new { e.TenantId, e.EmployeeNumber }).IsUnique();
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.IsActive });
        builder.HasIndex(e => new { e.TenantId, e.DepartmentId });

        builder.HasMany(e => e.JobFunctions)
            .WithOne()
            .HasForeignKey(j => j.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
