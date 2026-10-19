using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
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

        // The release-flow AuthorizationCatalogSync may already have filled the catalogue on
        // this empty database (it runs before this seeder, in every environment): reuse rows.
        var existingPermissions = await dbContext.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var permissions = PermissionCodes.All
            .Select(p => existingPermissions.TryGetValue(p.Code, out var existing)
                ? existing
                : new Permission { Id = Guid.NewGuid(), Code = p.Code, Module = p.Module, Action = p.Action, Description = p.Description })
            .ToList();
        dbContext.Permissions.AddRange(permissions.Where(p => !existingPermissions.ContainsKey(p.Code)));

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

        await dbContext.SaveChangesAsync();

        // The global qualification-type catalogue is reference data for every environment (the
        // startup sync normally created it already); add-if-missing keeps this seeder idempotent.
        await QualificationTypeSeeder.SyncAsync(dbContext);
    }
}
