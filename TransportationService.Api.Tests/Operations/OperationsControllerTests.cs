using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Operations.Controllers;
using TransportationService.Api.Modules.Operations.Dtos;
using TransportationService.Api.Modules.Operations.Services;

namespace TransportationService.Api.Tests.Operations;

/// <summary>
/// PERF-HIGH 4 at the HTTP boundary: the overview GET is a pure read and the alert
/// reconciliation is only reachable through the explicit POST, under the same right.
/// </summary>
public class OperationsControllerTests
{
    private sealed class RecordingSync : IAlertSyncService
    {
        public int Calls { get; private set; }

        public Task<AlertSyncResult> SyncAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(AlertSyncResult.Empty);
        }

        public Task<AlertSyncResult> SyncTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The controller must sync the request-scoped tenant only.");
    }

    private sealed class StubOverview : IOperationsOverviewService
    {
        public int Calls { get; private set; }

        public Task<OperationsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new OperationsOverviewDto(
                DateTime.UtcNow, new OperationsCountersDto(0, 0, 0, 0, 0, 0, 0), []));
        }
    }

    private static (OperationsController Controller, StubOverview Overview, RecordingSync Sync) Build()
    {
        var overview = new StubOverview();
        var sync = new RecordingSync();
        // IAlertService only serves the acknowledge/resolve/assign actions, which these tests never call.
        return (new OperationsController(overview, null!, sync), overview, sync);
    }

    [Fact]
    public async Task Overview_IsReadOnly_NeverRunsTheSync()
    {
        var (controller, overview, sync) = Build();

        var result = await controller.Overview(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, overview.Calls);
        Assert.Equal(0, sync.Calls);
    }

    [Fact]
    public async Task SyncAlerts_RunsTheSyncOnce_AndReturnsNoContent()
    {
        var (controller, overview, sync) = Build();

        var result = await controller.SyncAlerts(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, sync.Calls);
        Assert.Equal(0, overview.Calls);
    }

    [Fact]
    public void SyncAlerts_IsAPost_UnderTheOverviewRight()
    {
        var action = typeof(OperationsController).GetMethod(nameof(OperationsController.SyncAlerts))!;

        var route = action.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(route);
        Assert.Equal("alerts/sync", route.Template);

        var permission = action.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(permission);
        Assert.Equal(PermissionCodes.OperationsView, Assert.Single(permission.PermissionCodes));
    }
}
