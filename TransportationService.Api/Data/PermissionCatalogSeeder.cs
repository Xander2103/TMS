using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Keeps the persisted permission catalog in sync with <see cref="PermissionCodes.All"/> and
/// guarantees every system role (e.g. Administrator) is granted the full catalog. Idempotent
/// and safe to run on every startup, so newly-added permissions immediately reach existing
/// databases without a wipe/reseed.
/// </summary>
public static class PermissionCatalogSeeder
{
    public static async Task SyncAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Permissions.ToListAsync(cancellationToken);
        var existingByCode = existing.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var (code, module, action, description) in PermissionCodes.All)
        {
            if (existingByCode.TryGetValue(code, out var current))
            {
                // Keep metadata (module/action/description) authoritative from source.
                if (current.Module != module || current.Action != action || current.Description != description)
                {
                    current.Module = module;
                    current.Action = action;
                    current.Description = description;
                }
            }
            else
            {
                var added = new Permission { Id = Guid.NewGuid(), Code = code, Module = module, Action = action, Description = description };
                dbContext.Permissions.Add(added);
                existing.Add(added);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await GrantFullCatalogToSystemRolesAsync(dbContext, existing, cancellationToken);
    }

    private static async Task GrantFullCatalogToSystemRolesAsync(
        TransportationDbContext dbContext, List<Permission> allPermissions, CancellationToken cancellationToken)
    {
        var systemRoleIds = await dbContext.Roles
            .Where(r => r.IsSystemRole)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (systemRoleIds.Count == 0)
        {
            return;
        }

        var existingGrants = await dbContext.RolePermissions
            .Where(rp => systemRoleIds.Contains(rp.RoleId))
            .ToListAsync(cancellationToken);

        var grantLookup = existingGrants
            .Select(rp => (rp.RoleId, rp.PermissionId))
            .ToHashSet();

        var added = false;
        foreach (var roleId in systemRoleIds)
        {
            foreach (var permission in allPermissions)
            {
                if (grantLookup.Add((roleId, permission.Id)))
                {
                    dbContext.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id });
                    added = true;
                }
            }
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
