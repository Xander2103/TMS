using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TransportationService.Api.Modules.Attendance.Controllers;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Structurele bewaking van het attendance-aanvalsoppervlak: elk endpoint van de module
/// draagt expliciet [RequirePermission], behalve de drie kiosk-endpoints die BEWUST
/// [AllowAnonymous] zijn (per-request device-auth; zie Phase1-allowlist en
/// docs/attendance/security.md). Een nieuw attendance-endpoint zonder permissie faalt
/// hier vóór het de generieke Phase10-classificatie bereikt.
/// </summary>
public class AttendanceEndpointSecurityTests
{
    private static IEnumerable<(Type Controller, MethodInfo Action)> AttendanceActions() =>
        typeof(MyAttendanceController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract
                        && t.Namespace == typeof(MyAttendanceController).Namespace)
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (t, m)));

    [Fact]
    public void EveryAttendanceEndpoint_HasRequirePermission_OrIsTheReviewedKioskSurface()
    {
        var kioskSurface = new HashSet<string>(StringComparer.Ordinal)
        {
            "KioskPunchController.Ping",
            "KioskPunchController.Identify",
            "KioskPunchController.Punch",
        };

        var unprotected = new List<string>();
        foreach (var (controller, action) in AttendanceActions())
        {
            var name = $"{controller.Name}.{action.Name}";
            var hasPermission = action.GetCustomAttribute<RequirePermissionAttribute>() is not null
                                || controller.GetCustomAttribute<RequirePermissionAttribute>() is not null;
            if (kioskSurface.Contains(name))
            {
                // De kioskvlakken zijn anoniem-met-device-auth; die mogen juist GEEN
                // gebruikerspermissie dragen.
                Assert.False(hasPermission, $"{name} hoort device-auth te gebruiken, geen gebruikerspermissie.");
                continue;
            }

            if (!hasPermission)
            {
                unprotected.Add(name);
            }
        }

        Assert.True(unprotected.Count == 0,
            "Attendance-endpoints zonder [RequirePermission]: " + string.Join(", ", unprotected));
    }

    [Fact]
    public void KioskController_IsAnonymousWithKioskRateLimit_AndNothingElseInTheModuleIsAnonymous()
    {
        var kiosk = typeof(KioskPunchController);
        Assert.NotNull(kiosk.GetCustomAttribute<AllowAnonymousAttribute>());
        var rateLimit = kiosk.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
        Assert.NotNull(rateLimit);
        Assert.Equal("kiosk", rateLimit!.PolicyName);

        foreach (var (controller, action) in AttendanceActions().Where(a => a.Controller != kiosk))
        {
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }
}
