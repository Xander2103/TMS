using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Seeds and evolves the default business roles per tenant in three idempotent phases:
///
/// 1. BACKFILL — roles created by older versions of this seeder carry no TemplateCode yet;
///    non-system roles still bearing an ORIGINAL template display name are stamped once.
///    Only the six pre-HR names qualify: an exact "HR" on an old database can only be
///    tenant-created and is deliberately left alone (its name slot stays occupied).
/// 2. CREATE — templates without a stamped role are created with their full current
///    permission set, unless a same-named role occupies the slot (tenant-created; skipped).
/// 3. UPGRADE — versioned steps (DefaultRoleUpgrades) add newly introduced default
///    permissions to STAMPED roles exactly once per tenant; the applied version is
///    recorded in role_template_states. Nothing is ever removed, renames don't matter
///    (matching is by TemplateCode), and re-runs are no-ops — so tenant customisation,
///    including deleting an upgraded permission afterwards, always survives.
/// </summary>
public static class DefaultRoleSeeder
{
    /// <summary>Template names that existed BEFORE TemplateCode stamping (safe to backfill by exact name).</summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyBackfillNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Planner"] = "planner",
            ["Dispatcher"] = "dispatcher",
            ["Management"] = "management",
            ["Boekhouding"] = "boekhouding",
            ["Chauffeur"] = "chauffeur",
            ["Klantportaal"] = "klantportaal",
        };

    public static async Task SyncAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var tenantIds = await dbContext.Tenants.Select(t => t.Id).ToListAsync(cancellationToken);
        if (tenantIds.Count == 0)
        {
            return;
        }

        var permissionsByCode = await dbContext.Permissions.ToDictionaryAsync(p => p.Code, cancellationToken);
        var roles = await dbContext.Roles.ToListAsync(cancellationToken);
        var states = await dbContext.RoleTemplateStates.ToDictionaryAsync(s => s.TenantId, cancellationToken);
        var now = DateTime.UtcNow;
        var changed = false;

        // Phase 1 — backfill template identity on legacy seeded roles (exact name, non-system,
        // unstamped, and the code not already claimed by another role of the tenant).
        foreach (var role in roles.Where(r => !r.IsSystemRole && r.TemplateCode is null))
        {
            if (!LegacyBackfillNames.TryGetValue(role.Name, out var code))
            {
                continue;
            }

            if (roles.Any(other => other.TenantId == role.TenantId && other.TemplateCode == code))
            {
                continue;
            }

            role.TemplateCode = code;
            role.UpdatedAt = now;
            changed = true;
        }

        // Phase 2 — create missing template roles (full current permission set at creation).
        foreach (var tenantId in tenantIds)
        {
            foreach (var template in DefaultRoleDefinitions.All)
            {
                var hasStamped = roles.Any(r => r.TenantId == tenantId && r.TemplateCode == template.Code);
                var nameOccupied = roles.Any(r => r.TenantId == tenantId && r.Name == template.Name);
                if (hasStamped || nameOccupied)
                {
                    continue; // name-occupied without stamp = tenant-created role; leave it alone
                }

                var role = new Role
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = template.Name,
                    Description = template.Description,
                    IsSystemRole = false,
                    IsActive = true,
                    TemplateCode = template.Code,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                dbContext.Roles.Add(role);
                roles.Add(role);

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

        // Phase 3 — apply versioned template upgrades to stamped roles, once per tenant.
        var stampedRoleIds = roles.Where(r => r.TemplateCode is not null).Select(r => r.Id).ToList();
        var existingGrants = stampedRoleIds.Count == 0
            ? []
            : await dbContext.RolePermissions
                .Where(rp => stampedRoleIds.Contains(rp.RoleId))
                .Select(rp => new { rp.RoleId, rp.PermissionId })
                .ToListAsync(cancellationToken);
        var grantSet = existingGrants.Select(g => (g.RoleId, g.PermissionId)).ToHashSet();
        // Grants staged by phase 2 count too — a freshly created role must not be re-granted.
        foreach (var entry in dbContext.ChangeTracker.Entries<RolePermission>())
        {
            grantSet.Add((entry.Entity.RoleId, entry.Entity.PermissionId));
        }

        foreach (var tenantId in tenantIds)
        {
            states.TryGetValue(tenantId, out var state);
            var appliedVersion = state?.AppliedVersion ?? 1;

            foreach (var step in DefaultRoleUpgrades.Steps.OrderBy(s => s.Version))
            {
                if (step.Version <= appliedVersion)
                {
                    continue;
                }

                foreach (var (templateCode, codes) in step.GrantsByTemplateCode)
                {
                    var role = roles.FirstOrDefault(r => r.TenantId == tenantId && r.TemplateCode == templateCode);
                    if (role is null)
                    {
                        continue;
                    }

                    foreach (var code in codes)
                    {
                        if (!permissionsByCode.TryGetValue(code, out var permission)
                            || !grantSet.Add((role.Id, permission.Id)))
                        {
                            continue;
                        }

                        dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
                        changed = true;
                    }
                }
            }

            if (appliedVersion < DefaultRoleUpgrades.CurrentVersion)
            {
                if (state is null)
                {
                    dbContext.RoleTemplateStates.Add(new RoleTemplateState
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        AppliedVersion = DefaultRoleUpgrades.CurrentVersion,
                        UpdatedAt = now,
                    });
                }
                else
                {
                    state.AppliedVersion = DefaultRoleUpgrades.CurrentVersion;
                    state.UpdatedAt = now;
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
