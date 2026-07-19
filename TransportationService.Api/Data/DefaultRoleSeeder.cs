using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Seeds the default business roles (Planner, Dispatcher, Management, Boekhouding,
/// Chauffeur, Klantportaal) for every tenant that does not have a role with that name
/// yet. Permissions are granted at creation only — an existing role is never touched, so
/// tenant customisation (including deliberate renames/deletes… a deleted role IS
/// recreated, deactivating it instead prevents that) survives every startup.
/// </summary>
public static class DefaultRoleSeeder
{
    public static async Task SyncAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var tenantIds = await dbContext.Tenants.Select(t => t.Id).ToListAsync(cancellationToken);
        if (tenantIds.Count == 0)
        {
            return;
        }

        var permissionsByCode = await dbContext.Permissions.ToDictionaryAsync(p => p.Code, cancellationToken);
        var existingRoleNames = await dbContext.Roles
            .Select(r => new { r.TenantId, r.Name })
            .ToListAsync(cancellationToken);
        var existing = existingRoleNames
            .Select(r => (r.TenantId, r.Name))
            .ToHashSet();

        var changed = false;
        foreach (var tenantId in tenantIds)
        {
            foreach (var template in DefaultRoleDefinitions.All)
            {
                if (existing.Contains((tenantId, template.Name)))
                {
                    continue;
                }

                var role = new Role
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = template.Name,
                    Description = template.Description,
                    IsSystemRole = false,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                dbContext.Roles.Add(role);

                foreach (var code in template.PermissionCodes.Distinct())
                {
                    if (permissionsByCode.TryGetValue(code, out var permission))
                    {
                        dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
                    }
                }

                changed = true;
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
