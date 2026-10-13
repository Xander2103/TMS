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
        // Widened for application-level encryption (enc:v1:<base64>): ciphertext of a 15-char
        // NRN is ~90 chars. Plaintext legacy rows still fit.
        builder.Property(e => e.NationalRegisterNumber).HasMaxLength(200);
        builder.Property(e => e.PreferredLanguageCode).HasMaxLength(10);
        // Address/contact fields are optional (spec §13: only first/last name may block).
        builder.Property(e => e.Street).HasMaxLength(150);
        builder.Property(e => e.HouseNumber).HasMaxLength(20);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.CountryCode).HasMaxLength(2);
        builder.Property(e => e.PhoneNumber).HasMaxLength(30);
        builder.Property(e => e.MobilePhone).HasMaxLength(30);
        builder.Property(e => e.Email).HasMaxLength(250);
        builder.Property(e => e.EmergencyContactName).HasMaxLength(150);
        builder.Property(e => e.EmergencyContactPhone).HasMaxLength(30);
        builder.Property(e => e.Iban).HasMaxLength(34);
        builder.Property(e => e.Bic).HasMaxLength(11);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.EmploymentStatus).HasConversion<string>();
        builder.Property(e => e.CivilStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.IdentityCardNumber).HasMaxLength(200); // widened for encryption, see NRN
        builder.Property(e => e.DimonaNumber).HasMaxLength(30);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeNumber }).IsUnique();
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.IsActive });
        builder.HasIndex(e => new { e.TenantId, e.DepartmentId });

        builder.HasMany(e => e.JobFunctions)
            .WithOne()
            .HasForeignKey(j => j.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.EmergencyContacts)
            .WithOne()
            .HasForeignKey(c => c.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EmployeeEmergencyContactConfiguration : IEntityTypeConfiguration<EmployeeEmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmployeeEmergencyContact> builder)
    {
        builder.ToTable("employee_emergency_contacts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.Relationship).HasMaxLength(50);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.MobilePhone).HasMaxLength(30);
        builder.Property(c => c.Notes).HasMaxLength(500);

        builder.HasIndex(c => new { c.TenantId, c.EmployeeId });

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
