using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Services;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>One-line TripCostingService construction for tests that wire TripService by hand.</summary>
public static class CostingTestFactory
{
    public static TripCostingService Create(TransportationDbContext dbContext, ITenantContext tenant, TimeProvider clock)
    {
        var audit = new AuditService(dbContext, tenant, new DevCurrentUserContext(null));
        return new TripCostingService(
            dbContext, tenant, new DevCurrentUserContext(null), audit,
            new CostRateService(dbContext, tenant, audit), clock);
    }
}
