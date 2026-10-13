using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// Test convenience around the ONE dossier lifecycle path (confirmation sprint 2026-09-23):
/// confirm (= close) or reopen a dossier through <see cref="DossierLifecycleService"/>, so tests
/// that only need "a closed dossier" never grow a second way of closing one.
/// </summary>
public static class DossierTestLifecycle
{
    public static DossierLifecycleService Create(TransportationDbContext db, Guid tenantId, Guid? userId, TimeProvider clock)
    {
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db, tenant, new DevCurrentUserContext(userId));
        var dossiers = new DossierService(db, tenant, audit, clock);
        return new DossierLifecycleService(db, tenant, audit, clock, new DevCurrentUserContext(userId), () => dossiers, new DossierReadinessService(db, tenant));
    }

    /// <summary>Manual confirmation with every warning acknowledged (blockers still refuse).</summary>
    public static Task<DossierDetailDto?> CloseAsync(TransportationDbContext db, Guid tenantId, Guid dossierId, Guid? userId = null, TimeProvider? clock = null) =>
        Create(db, tenantId, userId, clock ?? TimeProvider.System)
            .ConfirmAsync(dossierId, new ConfirmDossierRequest(null, AcknowledgeWarnings: true), CancellationToken.None);

    public static Task<DossierDetailDto?> ReopenAsync(TransportationDbContext db, Guid tenantId, Guid dossierId, Guid? userId = null, TimeProvider? clock = null) =>
        Create(db, tenantId, userId, clock ?? TimeProvider.System)
            .ReopenAsync(dossierId, new ReopenDossierRequest("test"), CancellationToken.None);
}
