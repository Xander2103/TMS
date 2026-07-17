using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Data;

public static class MasterDataSeeder
{
    public static async Task SeedAsync(TransportationDbContext dbContext)
    {
        if (await dbContext.Tenants.AnyAsync())
        {
            return;
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Development Transportbedrijf",
            Slug = "dev",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.Tenants.Add(tenant);

        dbContext.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Timezone = "Europe/Amsterdam",
            DefaultLanguage = "nl",
            QualificationExpiryWarningDays = 30,
            DefaultPageSize = 25,
            EmployeeNumberPrefix = "MED-",
            EmployeeNumberNextValue = 1,
        });

        var permissions = PermissionCodes.All
            .Select(p => new Permission { Id = Guid.NewGuid(), Code = p.Code, Module = p.Module, Action = p.Action, Description = p.Description })
            .ToList();
        dbContext.Permissions.AddRange(permissions);

        var adminRole = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Administrator",
            Description = "Volledige toegang tot alle functies.",
            IsSystemRole = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.Roles.Add(adminRole);

        dbContext.RolePermissions.AddRange(permissions.Select(p => new RolePermission { RoleId = adminRole.Id, PermissionId = p.Id }));

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = "admin@dev.local",
            FirstName = "Dev",
            LastName = "Admin",
            PasswordHash = null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.Users.Add(adminUser);
        dbContext.UserRoles.Add(new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id });

        dbContext.QualificationTypes.AddRange(
            QualificationTypeRow(QualificationTypeCodes.DrivingLicenceB, "Rijbewijs B", "Rijbewijs", true),
            QualificationTypeRow(QualificationTypeCodes.DrivingLicenceC, "Rijbewijs C", "Rijbewijs", true),
            QualificationTypeRow(QualificationTypeCodes.DrivingLicenceCE, "Rijbewijs CE", "Rijbewijs", true),
            QualificationTypeRow(QualificationTypeCodes.Code95, "Code 95", "Certificaat", true),
            QualificationTypeRow(QualificationTypeCodes.Adr, "ADR", "Certificaat", true),
            QualificationTypeRow(QualificationTypeCodes.MedicalFitness, "Medische keuring", "Keuring", true),
            QualificationTypeRow(QualificationTypeCodes.DriverCard, "Bestuurderskaart", "Certificaat", true),
            QualificationTypeRow(QualificationTypeCodes.CraneCertificate, "Kraancertificaat", "Certificaat", true),
            QualificationTypeRow(QualificationTypeCodes.ForkliftCertificate, "Vorkheftruckcertificaat", "Certificaat", true),
            QualificationTypeRow(QualificationTypeCodes.Alfapass, "Alfapass", "Certificaat", true),
            QualificationTypeRow(QualificationTypeCodes.Other, "Overig", "Overig", false));

        await dbContext.SaveChangesAsync();
    }

    private static QualificationType QualificationTypeRow(string code, string name, string category, bool requiresExpiryDate) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        Category = category,
        RequiresExpiryDate = requiresExpiryDate,
        IsActive = true,
    };
}
