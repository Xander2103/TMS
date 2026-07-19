using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Tests.TestSupport;

public static class TripPackageTestFactory
{
    public static TripPackageService Create(TransportationDbContext dbContext, ITenantContext tenant, TimeProvider clock)
    {
        var currentUser = new DevCurrentUserContext(null);
        return new TripPackageService(
            dbContext, tenant, currentUser,
            new PackageEventWriter(dbContext, tenant, currentUser, clock),
            new AuditService(dbContext, tenant, currentUser), clock);
    }
}
