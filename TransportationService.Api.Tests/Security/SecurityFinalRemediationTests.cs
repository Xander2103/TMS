using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Eta.Controllers;
using TransportationService.Api.Modules.Eta.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Security final remediation (2026-10-18): driver trip ownership (H1), self password change
/// (M2), logout ending the session's access token (M3) and price projection for operational
/// roles (M1). Each test failed against the integrated base d07335d4.
/// </summary>
public class SecurityFinalRemediationTests
{
    private static readonly DateTimeOffset Now = new(2126, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record World(
        SqliteTestDbContext Db, Guid TenantId,
        Guid DriverAUserId, Guid DriverAId, Guid DriverBId, Guid TripA, Guid TripB,
        Guid PlannerUserId, Guid DispatcherUserId, Guid OfficeDriverUserId,
        Guid OrderA, Guid OrderB, Guid PackageB) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<World> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });

        var permissions = new Dictionary<string, Guid>();
        Guid Permission(string code)
        {
            if (!permissions.TryGetValue(code, out var id))
            {
                id = Guid.NewGuid();
                permissions[code] = id;
                db.Context.Permissions.Add(new Permission { Id = id, Code = code, Module = code.Split('.')[0], Action = code.Split('.')[1], Description = code });
            }

            return id;
        }

        Guid Role(string name, params string[] codes)
        {
            var roleId = Guid.NewGuid();
            db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = name, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
            foreach (var code in codes)
            {
                db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = Permission(code) });
            }

            return roleId;
        }

        var chauffeur = Role("Chauffeur", PermissionCodes.DriverWorkflowView, PermissionCodes.DriverWorkflowExecute,
            PermissionCodes.ScanningView, PermissionCodes.ScanningExecute);
        var planner = Role("Planner", PermissionCodes.PlanningView, PermissionCodes.PlanningEdit, PermissionCodes.OrdersView,
            PermissionCodes.OrdersEdit, PermissionCodes.DossiersView, PermissionCodes.DossiersPrice, PermissionCodes.TariffsView);
        var dispatcher = Role("Dispatcher", PermissionCodes.PlanningView, PermissionCodes.PlanningEdit, PermissionCodes.OrdersView,
            PermissionCodes.DossiersView, PermissionCodes.DriverWorkflowView, PermissionCodes.IncidentsManage);

        Guid User(string email, Guid? employeeId, params Guid[] roles)
        {
            var id = Guid.NewGuid();
            db.Context.Users.Add(new User
            {
                Id = id, TenantId = tenantId, Email = email, FirstName = "T", LastName = email, PasswordHash = "x",
                EmployeeId = employeeId, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            foreach (var role in roles)
            {
                db.Context.UserRoles.Add(new UserRole { UserId = id, RoleId = role });
            }

            return id;
        }

        Guid Driver(string number, out Guid employeeId)
        {
            employeeId = Guid.NewGuid();
            db.Context.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-" + number, FirstName = "Chauffeur", LastName = number,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            var driverId = Guid.NewGuid();
            db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-" + number, EmployeeId = employeeId, IsActive = true });
            return driverId;
        }

        var driverA = Driver("A", out var employeeA);
        var driverB = Driver("B", out _);
        var officeDriver = Driver("O", out var employeeO);
        var driverAUser = User("chauffeur.a@example.test", employeeA, chauffeur);
        var plannerUser = User("planner@example.test", null, planner);
        var dispatcherUser = User("dispatcher@example.test", null, dispatcher);
        // An office user who is also a driver keeps the office scope.
        var officeDriverUser = User("office.driver@example.test", employeeO, chauffeur, planner);

        var customerId = Guid.NewGuid();
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        Guid Order(string number, decimal price)
        {
            var id = Guid.NewGuid();
            db.Context.TransportOrders.Add(new TransportOrder
            {
                Id = id, TenantId = tenantId, CustomerId = customerId, OrderNumber = number, OrderDate = new(2126, 10, 18),
                Status = TransportOrderStatus.Planned, GoodsDescription = "Paletten", AgreedPrice = price,
            });
            return id;
        }

        var orderA = Order("ORD-A", 480m);
        var orderB = Order("ORD-B", 1234.56m);
        Guid Trip(string number, Guid driverId, Guid orderId)
        {
            var id = Guid.NewGuid();
            db.Context.Trips.Add(new Trip { Id = id, TenantId = tenantId, TripNumber = number, TripDate = new(2126, 10, 18), DriverId = driverId, Status = TripStatus.InProgress });
            db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = id, TransportOrderId = orderId, Sequence = 1 });
            return id;
        }

        var tripA = Trip("RIT-A", driverA, orderA);
        var tripB = Trip("RIT-B", driverB, orderB);
        var packageB = Guid.NewGuid();
        db.Context.Packages.Add(new Package { Id = packageB, TenantId = tenantId, TransportOrderId = orderB, PackageNumber = "COL-B", BarcodeValue = "COL-B", Description = "Pallet" });

        await db.Context.SaveChangesAsync();
        return new World(db, tenantId, driverAUser, driverA, driverB, tripA, tripB,
            plannerUser, dispatcherUser, officeDriverUser, orderA, orderB, packageB);
    }

    private static DriverTripScope Scope(World w, Guid userId) =>
        new(w.Db.Context, new DevTenantContext(w.TenantId), new DevCurrentUserContext(userId),
            new PermissionAuthorizationService(w.Db.Context));

    // ===================== H1 — driver ownership =====================

    [Fact]
    public async Task DriverScope_OwnTripOnly_OfficeScopeForPlanningHolders()
    {
        using var w = await SeedAsync();

        Assert.True(await Scope(w, w.DriverAUserId).CanAccessTripAsync(w.TripA, PermissionCodes.PlanningView, CancellationToken.None));
        Assert.False(await Scope(w, w.DriverAUserId).CanAccessTripAsync(w.TripB, PermissionCodes.PlanningView, CancellationToken.None));
        Assert.False(await Scope(w, w.DriverAUserId).CanAccessTripAsync(Guid.NewGuid(), PermissionCodes.PlanningView, CancellationToken.None));
        Assert.True(await Scope(w, w.PlannerUserId).CanAccessTripAsync(w.TripB, PermissionCodes.PlanningView, CancellationToken.None));
        Assert.True(await Scope(w, w.OfficeDriverUserId).CanAccessTripAsync(w.TripB, PermissionCodes.PlanningEdit, CancellationToken.None));

        Assert.False(await Scope(w, w.DriverAUserId).CanAccessPackageAsync(
            w.PackageB, [PermissionCodes.PackagesView, PermissionCodes.WarehouseView], CancellationToken.None));
    }

    [Fact]
    public async Task DriverScope_DeactivatedDriverProfile_ReachesNothing()
    {
        using var w = await SeedAsync();
        await w.Db.Context.Drivers.Where(d => d.Id == w.DriverAId).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));

        Assert.Null(await Scope(w, w.DriverAUserId).CurrentDriverIdAsync(CancellationToken.None));
        Assert.False(await Scope(w, w.DriverAUserId).CanAccessTripAsync(w.TripA, PermissionCodes.PlanningView, CancellationToken.None));
    }

    [Fact]
    public async Task Eta_ForeignTrip_GetAndDelayAnswer404_BeforeValidation_AndNeverReachTheService()
    {
        using var w = await SeedAsync();
        var eta = new RecordingEtaService();
        var controller = new EtaController(eta, Scope(w, w.DriverAUserId));

        Assert.IsType<NotFoundResult>((await controller.Get(w.TripB, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.SetDelay(w.TripB, new EtaController.SetDelayRequest(30, "file"), CancellationToken.None)).Result);
        // Out of range would be a 400 — but scope comes first, so the foreign trip stays a 404.
        Assert.IsType<NotFoundResult>((await controller.SetDelay(w.TripB, new EtaController.SetDelayRequest(-5, null), CancellationToken.None)).Result);
        Assert.Empty(eta.Calls);

        Assert.IsType<OkObjectResult>((await controller.Get(w.TripA, CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.SetDelay(w.TripA, new EtaController.SetDelayRequest(30, "file"), CancellationToken.None)).Result);
        Assert.Equal([$"get:{w.TripA}", $"delay:{w.TripA}:30"], eta.Calls);

        // Dispatch keeps its tenant-wide reach.
        var office = new EtaController(eta, Scope(w, w.PlannerUserId));
        Assert.IsType<OkObjectResult>((await office.SetDelay(w.TripB, new EtaController.SetDelayRequest(15, null), CancellationToken.None)).Result);
    }

    [Fact]
    public void DriverControllers_NeverAnswer403ForAnotherDriversTrip()
    {
        // 403 for "exists but not yours" vs 404 for "unknown" is an id oracle (H1 class).
        var root = RepoRoot();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "TransportationService.Api", "Modules"), "*Controller.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            Assert.False(
                System.Text.RegularExpressions.Regex.IsMatch(source, @"NotYourTrip\s*=>\s*StatusCode\(StatusCodes\.Status403Forbidden"),
                $"{Path.GetFileName(path)} maps NotYourTrip to 403.");
        }
    }

    [Fact]
    public void WarehouseStation_RequiresAWarehousePermission_NotTheDriversScanningRight()
    {
        static IReadOnlyList<string> Codes(Type controller, string action) =>
            controller.GetMethod(action)!.GetCustomAttributes(typeof(RequirePermissionAttribute), false)
                .Cast<RequirePermissionAttribute>().Single().PermissionCodes;

        var warehouse = typeof(TransportationService.Api.Modules.Warehousing.Controllers.WarehousesController);
        foreach (var codes in new[]
                 {
                     Codes(typeof(TransportationService.Api.Modules.Scanning.Controllers.WarehouseScansController), "Submit"),
                     Codes(warehouse, "Trace"),
                     Codes(warehouse, "Overview"),
                 })
        {
            Assert.DoesNotContain(PermissionCodes.ScanningExecute, codes);
            Assert.DoesNotContain(PermissionCodes.ScanningView, codes);
            Assert.Contains(PermissionCodes.WarehouseView, codes);
        }
    }

    // ===================== M3 / M2 — sessions =====================

    private sealed record AuthWorld(SqliteTestDbContext Db, AuthService Auth, TestClock Clock, Guid UserId) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private const string Password = "correct-horse-battery-staple";

    private static async Task<AuthWorld> SeedAuthAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        var hasher = new PasswordHasher();
        var userId = Guid.NewGuid();
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "user@example.test", FirstName = "F", LastName = "L",
            PasswordHash = hasher.Hash(Password), IsActive = true, SecurityStamp = Guid.NewGuid(),
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();
        var clock = new TestClock(Now);
        var auth = new AuthService(db.Context, hasher, AuthTestFactory.TokenService(clock), clock,
            Options.Create(new AuthenticationSecurityOptions()));
        return new AuthWorld(db, auth, clock, userId);
    }

    private static ClaimsPrincipal PrincipalOf(string accessToken) =>
        new(new ClaimsIdentity(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims, "test"));

    private static async Task<int?> RunFilterAsync(TransportationDbContext db, TimeProvider clock, ClaimsPrincipal principal)
    {
        var services = new ServiceCollection().AddSingleton(db).AddSingleton(clock).BuildServiceProvider();
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext { RequestServices = services, User = principal }, new RouteData(),
                new ActionDescriptor { EndpointMetadata = [] }), []);
        await new AccountStateAuthorizationFilter().OnAuthorizationAsync(context);
        return (context.Result as ObjectResult)?.StatusCode;
    }

    [Fact]
    public async Task Logout_EndsThatSessionsAccessTokenImmediately_OtherSessionsStaySignedIn()
    {
        using var w = await SeedAuthAsync();
        var laptop = (await w.Auth.LoginAsync("user@example.test", Password, CancellationToken.None)).Tokens!;
        var phone = (await w.Auth.LoginAsync("user@example.test", Password, CancellationToken.None)).Tokens!;
        var laptopPrincipal = PrincipalOf(laptop.AccessToken);
        Assert.NotNull(laptopPrincipal.FindFirst(AppClaimTypes.SessionId));
        Assert.Null(await RunFilterAsync(w.Db.Context, w.Clock, laptopPrincipal));

        await w.Auth.LogoutAsync(laptop.RefreshToken, w.UserId,
            Guid.Parse(laptopPrincipal.FindFirst(AppClaimTypes.SessionId)!.Value), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, await RunFilterAsync(w.Db.Context, w.Clock, laptopPrincipal));
        Assert.Null(await RunFilterAsync(w.Db.Context, w.Clock, PrincipalOf(phone.AccessToken)));
        // Replaying the logged-out refresh token is refused (and, as reuse, burns every session).
        Assert.Equal(AuthOutcome.InvalidCredentials, (await w.Auth.RefreshAsync(laptop.RefreshToken, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Logout_WithoutTheCookie_StillEndsTheSessionNamedByTheAccessToken()
    {
        using var w = await SeedAuthAsync();
        var login = (await w.Auth.LoginAsync("user@example.test", Password, CancellationToken.None)).Tokens!;
        var principal = PrincipalOf(login.AccessToken);

        await w.Auth.LogoutAsync(null, w.UserId, Guid.Parse(principal.FindFirst(AppClaimTypes.SessionId)!.Value), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, await RunFilterAsync(w.Db.Context, w.Clock, principal));
        Assert.Equal(AuthOutcome.InvalidCredentials, (await w.Auth.RefreshAsync(login.RefreshToken, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Refresh_KeepsTheSession_TheRotatedAccessTokenStaysValid()
    {
        using var w = await SeedAuthAsync();
        var login = (await w.Auth.LoginAsync("user@example.test", Password, CancellationToken.None)).Tokens!;
        var rotated = (await w.Auth.RefreshAsync(login.RefreshToken, CancellationToken.None)).Tokens!;

        Assert.Equal(
            PrincipalOf(login.AccessToken).FindFirst(AppClaimTypes.SessionId)!.Value,
            PrincipalOf(rotated.AccessToken).FindFirst(AppClaimTypes.SessionId)!.Value);
        // The pre-rotation access token belongs to the same live session until it expires.
        Assert.Null(await RunFilterAsync(w.Db.Context, w.Clock, PrincipalOf(login.AccessToken)));
        Assert.Null(await RunFilterAsync(w.Db.Context, w.Clock, PrincipalOf(rotated.AccessToken)));
    }

    [Fact]
    public async Task AccessToken_WithoutSessionClaim_IsRefused()
    {
        using var w = await SeedAuthAsync();
        var stamp = w.Db.Context.Users.Single(u => u.Id == w.UserId).SecurityStamp;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, w.UserId.ToString()), new Claim(AppClaimTypes.SecurityStamp, stamp.ToString())], "test"));

        Assert.Equal(StatusCodes.Status401Unauthorized, await RunFilterAsync(w.Db.Context, w.Clock, principal));
    }

    // ===================== M1 — prices for operational roles =====================

    private static TransportOrderService Orders(World w, Guid userId)
    {
        var tenant = new DevTenantContext(w.TenantId);
        var user = new DevCurrentUserContext(userId);
        return new TransportOrderService(w.Db.Context, tenant, new AuditService(w.Db.Context, tenant, user), new TestClock(Now),
            currentUser: user, permissionService: new PermissionAuthorizationService(w.Db.Context));
    }

    [Fact]
    public async Task OrderDetail_Dispatcher_GetsNoPrices_PlannerDoes()
    {
        using var w = await SeedAsync();

        var asDispatcher = (await Orders(w, w.DispatcherUserId).GetByIdAsync(w.OrderB, CancellationToken.None))!;
        Assert.Null(asDispatcher.AgreedPrice);
        Assert.Null(asDispatcher.CalculatedPrice);
        Assert.Null(asDispatcher.PricingSnapshot);
        Assert.Empty(asDispatcher.Invoices!);
        Assert.Equal("ORD-B", asDispatcher.OrderNumber); // operational data stays

        Assert.Equal(1234.56m, (await Orders(w, w.PlannerUserId).GetByIdAsync(w.OrderB, CancellationToken.None))!.AgreedPrice);
    }

    [Fact]
    public async Task Dossier_Dispatcher_GetsNoPricesInListOrDetail()
    {
        using var w = await SeedAsync();
        var dossierId = Guid.NewGuid();
        w.Db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = w.TenantId, DossierNumber = "DOS-1", Title = "Prijs", Status = DossierStatus.Open,
        });
        w.Db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = w.TenantId, DossierId = dossierId, TransportOrderId = w.OrderB });
        await w.Db.Context.SaveChangesAsync();

        DossierService Dossiers(Guid userId)
        {
            var tenant = new DevTenantContext(w.TenantId);
            var user = new DevCurrentUserContext(userId);
            return new DossierService(w.Db.Context, tenant, new AuditService(w.Db.Context, tenant, user), new TestClock(Now),
                permissionService: new PermissionAuthorizationService(w.Db.Context), currentUser: user);
        }

        var detail = (await Dossiers(w.DispatcherUserId).GetAsync(dossierId, CancellationToken.None))!;
        Assert.All(detail.Orders, o => Assert.Null(o.AgreedPrice));
        Assert.Equal(0m, detail.Financials.AgreedOrderTotal);
        Assert.All(await Dossiers(w.DispatcherUserId).ListAsync(null, null, null, CancellationToken.None),
            d => Assert.Null(d.AgreedPriceTotal));

        var asPlanner = (await Dossiers(w.PlannerUserId).GetAsync(dossierId, CancellationToken.None))!;
        Assert.Equal(1234.56m, Assert.Single(asPlanner.Orders).AgreedPrice);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransportationService.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class RecordingEtaService : IEtaService
    {
        public List<string> Calls { get; } = [];

        private static TripEtaDto Dto(Guid tripId) => new(tripId, "RIT", 0, null, []);

        public Task<TripEtaDto?> RecalculateTripAsync(Guid tripId, CancellationToken cancellationToken)
        {
            Calls.Add($"get:{tripId}");
            return Task.FromResult<TripEtaDto?>(Dto(tripId));
        }

        public Task<TripEtaDto?> SetTripDelayAsync(Guid tripId, int minutes, string? reason, CancellationToken cancellationToken)
        {
            Calls.Add($"delay:{tripId}:{minutes}");
            return Task.FromResult<TripEtaDto?>(Dto(tripId));
        }

        public Task<TripEtaDto?> OverrideStopEtaAsync(Guid tripId, Guid stopId, DateTime eta, string? note, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TripEtaDto?> ClearStopEtaOverrideAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StopEtaHistoryDto>?> GetStopEtaHistoryAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task NotifyTripStartedAsync(Guid tripId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
