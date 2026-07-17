using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Tests.TenantIsolation;

public class TenantContextTests
{
    [Fact]
    public void DevTenantContext_ExposesConfiguredTenantId()
    {
        var tenantId = Guid.NewGuid();
        var context = new DevTenantContext(tenantId);

        Assert.Equal(tenantId, context.TenantId);
    }
}
