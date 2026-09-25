using System.Data.Common;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Controllers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Final lifecycle integrity review (2026-09-23) — the legacy close alias, concurrent completion
/// events, reopen after cancel, the REAL delivery → auto-confirm path, and confirmation vs invoicing.
/// </summary>
public class DossierLifecycleIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid EmployeeId, Guid DriverId, Guid DriverUserId)
    {
        public DossierLifecycleService Lifecycle(TransportationService.Api.Data.TransportationDbContext? context = null, Guid? userId = null, Guid? tenantId = null)
        {
            var ctx = context ?? Db.Context;
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var user = new DevCurrentUserContext(userId ?? UserId);
            var audit = new AuditService(ctx, tenant, user);
            var dossiers = new DossierService(ctx, tenant, audit, new TestClock(Now));
            return new DossierLifecycleService(ctx, tenant, audit, new TestClock(Now), user, () => dossiers, new DossierReadinessService(ctx, tenant));
        }

        public DossierService Dossiers() =>
            new(Db.Context, new DevTenantContext(TenantId), new AuditService(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(UserId)), new TestClock(Now));

        public TransportOrderService Orders(DossierLifecycleService? lifecycle = null) =>
            new(Db.Context, new DevTenantContext(TenantId), new AuditService(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(UserId)), new TestClock(Now),
                permissionService: new AllowAll(), currentUser: new DevCurrentUserContext(UserId), dossierLifecycle: lifecycle);

        public Task<TransportDossier> Entity(Guid id) => Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == id);

        public Task<List<Modules.Auditing.Entities.AuditLog>> Audits(Guid id, string action) =>
            Db.Context.AuditLogs.AsNoTracking().Where(a => a.EntityType == "TransportDossier" && a.EntityId == id.ToString() && a.Action == action).ToListAsync();

        public async Task<Guid> TypeIdAsync(string code) => (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        /// <summary>Dossier + one transport activity/order per status, with a loading + unloading stop each.</summary>
        public async Task<(Guid DossierId, List<(Guid OrderId, Guid LoadStopId, Guid UnloadStopId)> Orders)> DossierWithOrdersAsync(params TransportOrderStatus[] statuses)
        {
            var dossier = await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId), CancellationToken.None);
            var typeId = await TypeIdAsync("DIRECT_TRANSPORT");
            var orders = new List<(Guid, Guid, Guid)>();
            var sequence = 1;
            foreach (var status in statuses)
            {
                var orderId = Guid.NewGuid();
                var load = Guid.NewGuid();
                var unload = Guid.NewGuid();
                Db.Context.TransportOrders.Add(new TransportOrder { Id = orderId, TenantId = TenantId, CustomerId = CustomerId, OrderNumber = $"ORD-{sequence}", OrderDate = new(2026, 9, 22), Status = status, GoodsDescription = "Paletten" });
                Db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = TenantId, DossierId = dossier.Id, TransportOrderId = orderId });
                Db.Context.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = TenantId, DossierId = dossier.Id, ActivityTypeId = typeId, Sequence = sequence, LinkedTransportOrderId = orderId });
                Db.Context.TransportOrderStops.AddRange(
                    new TransportOrderStop { Id = load, TenantId = TenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                    new TransportOrderStop { Id = unload, TenantId = TenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" });
                orders.Add((orderId, load, unload));
                sequence++;
            }

            await Db.Context.SaveChangesAsync();
            return (dossier.Id, orders);
        }

        /// <summary>The driver's own InProgress trip carrying the order — the state a delivery starts from.</summary>
        public async Task<Guid> InProgressTripAsync(string number, Guid orderId)
        {
            var tripId = Guid.NewGuid();
            Db.Context.Trips.Add(new Trip { Id = tripId, TenantId = TenantId, TripNumber = number, TripDate = new(2026, 9, 23), DriverId = DriverId, Status = TripStatus.InProgress });
            Db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = TenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
            await Db.Context.SaveChangesAsync();
            return tripId;
        }

        public TripExecutionService Execution(DossierLifecycleService lifecycle)
        {
            var tenant = new DevTenantContext(TenantId);
            var clock = new TestClock(Now);
            var user = new DevCurrentUserContext(DriverUserId);
            var audit = new AuditService(Db.Context, tenant, user);
            var planningSync = new TripPlanningSyncService(Db.Context, tenant);
            var trips = new TripService(Db.Context, tenant, audit,
                new PlanningConflictService(Db.Context, tenant, new QualificationStatusCalculator(), clock),
                new NotificationService(Db.Context, tenant, user, clock), planningSync,
                CostingTestFactory.Create(Db.Context, tenant, clock), TripPackageTestFactory.Create(Db.Context, tenant, clock),
                dossierLifecycle: lifecycle);
            return new TripExecutionService(Db.Context, tenant, user, audit, trips, planningSync,
                TripPackageTestFactory.Create(Db.Context, tenant, clock), new NotificationService(Db.Context, tenant, user, clock), clock);
        }
    }

    private sealed class AllowAll : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static async Task<Harness> SeedAsync(params IInterceptor[] extraInterceptors)
    {
        var db = new SqliteTestDbContext(null, extraInterceptors);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR", TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
        });
        var entityId = Guid.NewGuid();
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme BV", IsActive = true, IsDefault = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV", VatNumber = "BE0123456789", IsActive = true, DefaultLegalEntityId = entityId });
        db.Context.Users.Add(new User { Id = UserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Users.Add(new User { Id = driverUserId, TenantId = tenantId, Email = "chauffeur@acme.be", PasswordHash = "x", FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId, employeeId, driverId, driverUserId);
    }

    // ------------------------------------------------------------ 1. legacy /close alias

    [Fact]
    public void CloseAlias_CarriesTheSamePermission_AsConfirm()
    {
        var close = typeof(DossiersController).GetMethod(nameof(DossiersController.Close))!.GetCustomAttribute<RequirePermissionAttribute>()!;
        var confirm = typeof(DossiersController).GetMethod(nameof(DossiersController.Confirm))!.GetCustomAttribute<RequirePermissionAttribute>()!;
        Assert.Equal(confirm.PermissionCodes, close.PermissionCodes);
        Assert.Equal([PermissionCodes.DossiersManage], close.PermissionCodes);
    }

    [Fact]
    public async Task CloseAlias_RunsTheLifecycleService_BlockersMetadataAuditAndTenantIncluded()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Draft);
        var controller = new DossiersController(h.Dossiers(), null!, null!, null!, null!, h.Lifecycle());

        // A real blocker is enforced through the alias exactly as through /confirm.
        h.Db.Context.Incidents.Add(new Incident { Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = dossierId, Title = "Schade", Status = IncidentStatus.New });
        await h.Db.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => controller.Close(dossierId, CancellationToken.None));
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossierId)).Status);

        var incident = h.Db.Context.Incidents.Single();
        incident.Status = IncidentStatus.Resolved;
        await h.Db.Context.SaveChangesAsync();

        // Warnings are acknowledged implicitly (pre-sprint semantics), and the result is a MANUAL
        // confirmation with full metadata and the new audit action — never a parallel "Closed" flow.
        var closed = Assert.IsType<DossierDetailDto>(Assert.IsType<OkObjectResult>((await controller.Close(dossierId, CancellationToken.None)).Result).Value);
        Assert.Equal(("Closed", "Manual", UserId), (closed.Status, closed.ConfirmationSource, closed.ConfirmedByUserId));
        Assert.Single(await h.Audits(dossierId, "ConfirmedManually"));
        Assert.Empty(await h.Audits(dossierId, "Closed"));

        // Idempotent (no second audit) and tenant-scoped (404 elsewhere).
        Assert.IsType<OkObjectResult>((await controller.Close(dossierId, CancellationToken.None)).Result);
        Assert.Single(await h.Audits(dossierId, "ConfirmedManually"));
        var foreign = new DossiersController(h.Dossiers(), null!, null!, null!, null!, h.Lifecycle(tenantId: Guid.NewGuid()));
        Assert.IsType<NotFoundResult>((await foreign.Close(dossierId, CancellationToken.None)).Result);
    }

    // ------------------------------------------------------------ 2. concurrent auto-confirm

    /// <summary>
    /// The second completion event's lifecycle service already evaluated the dossier as Open; right
    /// before its own dossier UPDATE hits the database, the first event's confirmation runs to
    /// completion through another context. Exactly one confirmation may survive.
    /// </summary>
    private sealed class CompetingConfirmationInterceptor : DbCommandInterceptor
    {
        public Func<Task>? Competitor { get; set; }
        public int Fired { get; private set; }

        private async Task RaceAsync(DbCommand command)
        {
            if (Competitor is { } competitor && Fired == 0 && command.CommandText.Contains("transport_dossiers", StringComparison.Ordinal)
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                Fired++;
                await competitor();
            }
        }

        // EF Core issues UPDATEs through ExecuteReader (RETURNING) or ExecuteNonQuery depending on provider/batch.
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RaceAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await RaceAsync(command);
            return result;
        }
    }

    [Fact]
    public async Task TwoNearlySimultaneousCompletionEvents_ConfirmExactlyOnce()
    {
        var interceptor = new CompetingConfirmationInterceptor();
        var h = await SeedAsync(interceptor);
        using var _ = h.Db;
        var (dossierId, orders) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed, TransportOrderStatus.Completed);

        // Request 1 works on a second context over the same database; request 2 on the intercepted one.
        await using var other = h.Db.CreateContextForTenant(h.TenantId);
        var request1 = h.Lifecycle(other);
        var request2 = h.Lifecycle();
        var request1Result = false;
        interceptor.Competitor = async () => request1Result = await request1.TryAutoConfirmForOrdersAsync([orders[0].OrderId], "trip:RIT-A", CancellationToken.None) == 1;

        var request2Result = await request2.TryAutoConfirmForOrdersAsync([orders[1].OrderId], "trip:RIT-B", CancellationToken.None);

        Assert.Equal(1, interceptor.Fired);
        Assert.True(request1Result);
        Assert.Equal(0, request2Result);
        var entity = await h.Entity(dossierId);
        Assert.Equal((DossierStatus.Closed, DossierConfirmationSource.Automatic, Now.UtcDateTime), (entity.Status, entity.ConfirmationSource, entity.ClosedAt));
        var audit = Assert.Single(await h.Audits(dossierId, "ConfirmedAutomatically"));
        Assert.Contains("trip:RIT-A", audit.NewValuesJson);
    }

    // ------------------------------------------------------------ 3. reopen after cancel

    [Fact]
    public async Task CancelWithDraftOrders_ThenReopen_KeepsTheOrdersCancelled_AndStaysPredictable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, orders) = await h.DossierWithOrdersAsync(TransportOrderStatus.Draft, TransportOrderStatus.Draft);

        await h.Lifecycle().CancelAsync(dossierId, new CancelDossierRequest("Klant zegt af"), CancellationToken.None);
        Assert.All(orders, o => Assert.Equal(TransportOrderStatus.Cancelled, h.Db.Context.TransportOrders.AsNoTracking().Single(x => x.Id == o.OrderId).Status));

        var reopened = await h.Lifecycle().ReopenAsync(dossierId, new ReopenDossierRequest("Toch uitvoeren"), CancellationToken.None);

        // Dossier Open again; the orders are NOT silently reactivated — they stay Cancelled and say so.
        Assert.Equal("Open", reopened!.Status);
        Assert.All(orders, o => Assert.Equal(TransportOrderStatus.Cancelled, h.Db.Context.TransportOrders.AsNoTracking().Single(x => x.Id == o.OrderId).Status));
        Assert.All(reopened.Activities!, a => Assert.Equal("Cancelled", a.LinkedOrderStatus));
        var audit = Assert.Single(await h.Audits(dossierId, "Reopened"));
        Assert.Contains("Cancelled", audit.OldValuesJson);
        Assert.Contains("Klant zegt af", audit.OldValuesJson);
        Assert.Single(await h.Audits(dossierId, "Cancelled"));

        // Predictable afterwards: nothing executed → never auto-confirmed; manual confirm only with acknowledgement.
        var evaluation = await h.Lifecycle().EvaluateAsync(dossierId, CancellationToken.None);
        Assert.False(evaluation!.CanAutoConfirm);
        Assert.Contains(evaluation.AutoBlockers, b => b.Code == "nothing.completed");
        Assert.True(evaluation.CanConfirmManually);
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossierId, "x", CancellationToken.None));

        // The EXISTING per-order corrective transition (Cancelled → Draft, with reason) is the way back.
        var restored = await h.Orders().CorrectStatusAsync(orders[0].OrderId, TransportOrderStatus.Draft, "Dossier heropend", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, restored.Outcome);
        Assert.Equal(TransportOrderStatus.Draft, h.Db.Context.TransportOrders.AsNoTracking().Single(x => x.Id == orders[0].OrderId).Status);
    }

    // ------------------------------------------------------------ 4. real delivery flow

    [Fact]
    public async Task LastDeliveryStopCompleted_ThroughTripExecution_ConfirmsTheDossierAutomatically()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, orders) = await h.DossierWithOrdersAsync(TransportOrderStatus.InProgress, TransportOrderStatus.InProgress);
        var tripA = await h.InProgressTripAsync("RIT-A", orders[0].OrderId);
        var tripB = await h.InProgressTripAsync("RIT-B", orders[1].OrderId);
        var execution = h.Execution(h.Lifecycle());

        async Task DeliverAsync(Guid tripId, (Guid OrderId, Guid LoadStopId, Guid UnloadStopId) order)
        {
            var loaded = await execution.CompleteAsync(tripId, order.LoadStopId, new CompleteStopRequest(null, null), true, false, CancellationToken.None);
            Assert.Equal(ExecutionOutcome.Success, loaded.Outcome);
            var delivered = await execution.CompleteAsync(tripId, order.UnloadStopId, new CompleteStopRequest("Klant", null), true, false, CancellationToken.None);
            Assert.Equal(ExecutionOutcome.Success, delivered.Outcome);
        }

        // First delivery: its trip auto-completes and its order becomes Completed — the dossier stays Open.
        await DeliverAsync(tripA, orders[0]);
        Assert.Equal(TripStatus.Completed, (await h.Db.Context.Trips.AsNoTracking().SingleAsync(t => t.Id == tripA)).Status);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == orders[0].OrderId)).Status);
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossierId)).Status);

        // Last delivery: same path, and now every relevant part is done → automatically confirmed, once.
        await DeliverAsync(tripB, orders[1]);
        var entity = await h.Entity(dossierId);
        Assert.Equal((DossierStatus.Closed, DossierConfirmationSource.Automatic, (Guid?)null), (entity.Status, entity.ConfirmationSource, entity.ConfirmedByUserId));
        var audit = Assert.Single(await h.Audits(dossierId, "ConfirmedAutomatically"));
        Assert.Contains("trip:RIT-B", audit.NewValuesJson);
    }

    // ------------------------------------------------------------ 6. confirmation vs invoice

    [Fact]
    public async Task ConfirmedDossier_StaysInvoiceable_AndInvoicingNeverTouchesTheConfirmation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, orders) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orders[0].OrderId);
        order.AgreedPrice = 300m;
        order.PriceIsManual = true;
        await h.Db.Context.SaveChangesAsync();

        Assert.True(await h.Lifecycle().TryAutoConfirmAsync(dossierId, "trip:RIT-1", CancellationToken.None));
        Assert.Empty(await h.Db.Context.Invoices.AsNoTracking().ToListAsync());

        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(UserId));
        var invoices = new InvoiceService(h.Db.Context, tenant, audit, new TestClock(Now), new InvoiceNumberService(h.Db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(h.Db.Context, tenant, audit, new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(h.Db.Context, tenant, audit));

        // Still a candidate after confirmation; the invoice is created by the invoicing flow only.
        Assert.Contains(await invoices.ListUninvoicedOrdersAsync(h.CustomerId, CancellationToken.None), o => o.Id == orders[0].OrderId);
        var created = await invoices.CreateAsync(new CreateInvoiceRequest(h.CustomerId, new DateOnly(2026, 9, 23), [orders[0].OrderId], [], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);

        var afterInvoice = await h.Entity(dossierId);
        Assert.Equal((DossierStatus.Closed, DossierConfirmationSource.Automatic), (afterInvoice.Status, afterInvoice.ConfirmationSource));
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == orders[0].OrderId)).Status);

        // Cancelling the draft invoice releases the order, not the dossier's confirmation.
        Assert.Equal(InvoiceOperationOutcome.Success, (await invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Cancelled, CancellationToken.None)).Outcome);
        Assert.Equal(DossierStatus.Closed, (await h.Entity(dossierId)).Status);
        Assert.Single(await h.Audits(dossierId, "ConfirmedAutomatically"));
    }
}
