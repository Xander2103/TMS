using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TransportationService.Api.Data;

/// <summary>
/// Release-flow synchronisation of the authorization data that the code defines: the permission
/// catalogue (<see cref="PermissionCatalogSeeder"/>) and the versioned role templates
/// (<see cref="DefaultRoleSeeder"/>), in that order — a role grant needs its Permission row.
///
/// Runs in EVERY environment at startup, right after the migrations the deploy applied. Both
/// steps are idempotent and tenant-safe (a tenant's own customisation is never overridden), so
/// a release needs no manual database work for a new permission code or a role upgrade.
///
/// Closure sprint 2026-09-23: until now this ran only inside the Development block of
/// Program.cs, which left production at role template v24 with ~20 codes (dossiers.price,
/// attendance.*, backups.*, …) missing from its catalogue.
/// </summary>
public static class AuthorizationCatalogSync
{
    public static async Task SyncAsync(TransportationDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        var codesBefore = await dbContext.Permissions.CountAsync(cancellationToken);
        var versionsBefore = await dbContext.RoleTemplateStates.AsNoTracking()
            .Select(s => s.AppliedVersion).ToListAsync(cancellationToken);
        var lowestBefore = versionsBefore.Count == 0 ? (int?)null : versionsBefore.Min();

        await PermissionCatalogSeeder.SyncAsync(dbContext, cancellationToken);
        await DefaultRoleSeeder.SyncAsync(dbContext, cancellationToken);

        var codesAfter = await dbContext.Permissions.CountAsync(cancellationToken);
        if (codesAfter != codesBefore || lowestBefore is { } lowest && lowest < DefaultRoleUpgrades.CurrentVersion)
        {
            logger.LogInformation(
                "Authorization catalogue synced: permissions {Before} -> {After}, role templates {LowestBefore} -> v{Current}",
                codesBefore, codesAfter, lowestBefore is null ? "none" : $"v{lowestBefore}", DefaultRoleUpgrades.CurrentVersion);
        }
        else
        {
            logger.LogDebug("Authorization catalogue already current ({Count} permissions, role templates v{Current})",
                codesAfter, DefaultRoleUpgrades.CurrentVersion);
        }
    }
}
