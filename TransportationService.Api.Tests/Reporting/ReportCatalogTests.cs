using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reporting;
using TransportationService.Api.Modules.Reporting.Controllers;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reporting;

public class ReportCatalogTests
{
    [Fact]
    public void Catalog_DefinitionsAreConsistent()
    {
        Assert.Equal(ReportCatalog.All.Count, ReportCatalog.All.Select(r => r.Id).Distinct().Count());

        foreach (var report in ReportCatalog.All)
        {
            Assert.NotEmpty(report.Permissions);
            switch (report.Kind)
            {
                case ReportKind.Export:
                    Assert.False(string.IsNullOrWhiteSpace(report.Endpoint));
                    Assert.Contains(report.FileType, new[] { "xlsx", "csv" });
                    break;
                case ReportKind.Page:
                    Assert.False(string.IsNullOrWhiteSpace(report.Route));
                    break;
                case ReportKind.ComingSoon:
                    Assert.Null(report.Endpoint);
                    Assert.Null(report.Route);
                    break;
            }
        }
    }

    [Fact]
    public async Task List_OnlyReturnsReportsTheUserMayOpen()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var packageReportsPermissionId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = "t", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "u@t.be", FirstName = "U", LastName = "Ser", IsActive = true });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Rapporteur", IsActive = true });
        db.Context.Permissions.Add(new Permission
        {
            Id = packageReportsPermissionId, Code = "package_reports.export", Module = "package_reports", Action = "export", Description = "x",
        });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = packageReportsPermissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await db.Context.SaveChangesAsync();

        var controller = new ReportCatalogController(
            new PermissionSetService(db.Context), new DevCurrentUserContext(userId));
        var result = await controller.List(CancellationToken.None);
        var entries = Assert.IsAssignableFrom<IReadOnlyList<ReportCatalogEntryDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        // Package reports are visible, KPI exports (kpi.export) and pages the user lacks are not.
        Assert.Contains(entries, e => e.Id == "order-packages");
        Assert.Contains(entries, e => e.Id == "scan-activity");
        Assert.DoesNotContain(entries, e => e.Id == "kpi-trip-profitability");
        Assert.DoesNotContain(entries, e => e.Id == "kpi-dashboard");
        Assert.DoesNotContain(entries, e => e.Id == "customer-overview");
    }
}
