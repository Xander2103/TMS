using TransportationService.Api.Modules.Eligibility.Dtos;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Eligibility;

public class EligibilityOverrideServiceTests
{
    [Fact]
    public async Task CreateAsync_Throws_WhenReasonIsBlank()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var tenantContext = new DevTenantContext(tenantId);
        var auditService = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(approverId));
        var sut = new EligibilityOverrideService(db.Context, tenantContext, auditService);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateAsync(
            approverId,
            new CreateEligibilityOverrideRequest(Guid.NewGuid(), "TransportOrder", null, "AdrRequired", "   ", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RecordsApprovingUser_AndWritesAuditLog()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var tenantContext = new DevTenantContext(tenantId);
        var auditService = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(approverId));
        var sut = new EligibilityOverrideService(db.Context, tenantContext, auditService);

        var result = await sut.CreateAsync(
            approverId,
            new CreateEligibilityOverrideRequest(Guid.NewGuid(), "TransportOrder", null, "AdrRequired", "Klant heeft alternatieve begeleiding geregeld.", null),
            CancellationToken.None);

        Assert.Equal(approverId, result.ApprovedByUserId);
        Assert.Single(db.Context.AuditLogs);
    }
}
