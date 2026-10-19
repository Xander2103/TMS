using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TransportationService.Api.Data;

/// <summary>
/// Production hardening (B3): the add-if-missing REFERENCE seeders run at startup in EVERY
/// environment, so existing tenants pick up newly introduced codes (contract types STUD/FLEXI,
/// stock units, languages, expiry policies, …) without a Development start — which is what
/// <see cref="ReferenceDataSeeder"/> always documented but only Development ever did.
///
/// What runs here, and why it is safe on a live database:
/// - <see cref="QualificationTypeSeeder"/>: the global qualification catalogue (codes only).
/// - <see cref="ReferenceDataSeeder"/> WITHOUT the starter catalogues: code lists only
///   (departments, unit types, languages, contract types, …). Seed-if-empty / add-if-missing,
///   soft-deleted rows count as present, so tenant curation is never undone.
/// - <see cref="ExpiryPolicySeeder"/>: default reminder policies, add-if-missing per (kind, code).
/// - <see cref="LegalEntitySeeder"/>: only for a tenant WITHOUT any legal entity, built from that
///   tenant's own company settings (never invented data).
///
/// Deliberately NOT here: <see cref="MasterDataSeeder"/> / <see cref="DevAdminSeeder"/> /
/// <see cref="DevDemoDataSeeder"/> (Development tenant, admin@dev.local, demo records), the
/// priced service-option starter list and the issued-item starter catalogue (new tenants get
/// those from provisioning), and the legacy rate-card conversion (a data migration, not reference
/// data). Reference data is not worth a crash loop: a failure is logged per tenant and startup
/// continues.
/// </summary>
public static class TenantReferenceDataSync
{
    public static async Task SyncAsync(TransportationDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        await RunGuardedAsync(dbContext, logger, "qualification types", null,
            () => QualificationTypeSeeder.SyncAsync(dbContext, cancellationToken));

        var tenantIds = await dbContext.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
        {
            await RunGuardedAsync(dbContext, logger, "reference lists", tenantId,
                () => ReferenceDataSeeder.SeedTenantAsync(dbContext, tenantId, includeStarterCatalogs: false, cancellationToken));
            await RunGuardedAsync(dbContext, logger, "expiry policies", tenantId,
                () => ExpiryPolicySeeder.SeedTenantAsync(dbContext, tenantId, cancellationToken));
        }

        await RunGuardedAsync(dbContext, logger, "default legal entities", null,
            () => LegalEntitySeeder.SeedAsync(dbContext));
    }

    private static async Task RunGuardedAsync(
        TransportationDbContext dbContext, ILogger logger, string step, Guid? tenantId, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Drop whatever the failed step staged so the next step starts from a clean context.
            dbContext.ChangeTracker.Clear();
            logger.LogError(exception,
                "Reference data sync step '{Step}' failed for tenant {TenantId}; startup continues.",
                step, tenantId?.ToString() ?? "(all)");
        }
    }
}
