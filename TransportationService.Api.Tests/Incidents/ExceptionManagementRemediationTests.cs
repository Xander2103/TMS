using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Exceptions.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Exceptions.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Incidents;

/// <summary>
/// Exception-management remediation (2026-10-18): EX-01 never-destructive incident edits, EX-02
/// resolved templates + placeholder guard, EX-03 vehicle problems → fleet damage, EX-04 one
/// incident scope for dossier confirmation, EX-05 the severity policy for open afwijkingen,
/// EX-06/07 permissions, EX-08 the customer-facing delivery outcome, plus escalation, redelivery
/// and photo retention.
/// </summary>
public class ExceptionManagementRemediationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed class Harness : IDisposable
    {
        public required SqliteTestDbContext Db { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid OtherCustomerId { get; init; }
        public required Guid DispatcherId { get; init; }
        public required Guid DriverUserId { get; init; }
        public required Guid FleetUserId { get; init; }
        public required Guid PortalUserId { get; init; }
        public required Guid DriverId { get; init; }
        public required Guid VehicleId { get; init; }
        public required Guid DossierId { get; init; }
        public required Guid OrderId { get; init; }
        public required Guid UnloadingStopId { get; init; }
        public required Guid TripId { get; init; }
        public required Guid OtherDossierId { get; init; }
        public required Guid OtherOrderId { get; init; }
        public PermissionSet Permissions { get; } = new();
        public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "ts-ex-remediation", Guid.NewGuid().ToString("N"));

        private DevTenantContext Tenant => new(TenantId);
        private TestClock Clock => new(Now);

        public NotificationService Notifications(Guid? user = null) =>
            new(Db.Context, Tenant, new DevCurrentUserContext(user ?? DispatcherId), Clock);

        public NotificationEventService Events()
        {
            var audit = new AuditService(Db.Context, Tenant, new DevCurrentUserContext(DispatcherId));
            return new NotificationEventService(Db.Context, Tenant, new MessageOutboxService(Db.Context, Tenant, Clock),
                Notifications(), new CustomerCommunicationService(Db.Context, Tenant, audit),
                NullLogger<NotificationEventService>.Instance);
        }

        public IncidentService Incidents(Guid? user = null)
        {
            var current = new DevCurrentUserContext(user ?? DispatcherId);
            return new IncidentService(Db.Context, Tenant, new AuditService(Db.Context, Tenant, current),
                Notifications(user), Clock, Permissions, current, Events());
        }

        public DriverIncidentService DriverIncidents() =>
            new(Db.Context, Tenant, new DevCurrentUserContext(DriverUserId), Incidents(DriverUserId), Notifications(DriverUserId));

        public ExecutionExceptionService Exceptions(Guid? user = null)
        {
            var current = new DevCurrentUserContext(user ?? DispatcherId);
            return new ExecutionExceptionService(Db.Context, Tenant, current, new AuditService(Db.Context, Tenant, current),
                Notifications(user), new LocalFileStorageService(StorageRoot), Clock);
        }

        public ProblemFollowUpService FollowUp(Guid? user = null)
        {
            var current = new DevCurrentUserContext(user ?? DispatcherId);
            var audit = new AuditService(Db.Context, Tenant, current);
            return new ProblemFollowUpService(Db.Context, Tenant, Incidents(user),
                new DamageReportService(Db.Context, Tenant, audit), audit,
                new AuditHistoryReader(Db.Context, Tenant, new AuditReferenceResolver(Db.Context, Tenant)),
                current, Permissions);
        }

        public DossierLifecycleService Lifecycle() => DossierTestLifecycle.Create(Db.Context, TenantId, DispatcherId, Clock);

        public Task<int> NotificationsOf(Guid userId, string type) =>
            Db.Context.Notifications.CountAsync(n => n.UserId == userId && n.Type == type);

        public void Dispose()
        {
            Db.Dispose();
            if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var dispatcherId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var fleetUserId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1, DefaultLanguage = "nl",
        });
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme BV", IsActive = true, IsDefault = true });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", Email = "klant@haven.be", IsActive = true, DefaultLegalEntityId = entityId },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Ander NV", IsActive = true, DefaultLegalEntityId = entityId });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Chauffeur",
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-EXC-001", IsActive = true });
        db.Context.Users.AddRange(
            User(dispatcherId, tenantId, "dispatch@acme.be", "Dina", "Dispatch"),
            User(driverUserId, tenantId, "jan@acme.be", "Jan", "Chauffeur", employeeId),
            User(fleetUserId, tenantId, "vloot@acme.be", "Frank", "Vloot"),
            new User
            {
                Id = portalUserId, TenantId = tenantId, Email = "portaal@haven.be", PasswordHash = "x", FirstName = "Klaas",
                LastName = "Klant", CustomerId = customerId, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);

        // Role/permission rows: dispatcher handles problems, fleet edits damage, the portal user holds a (legacy) internal grant.
        await GrantAsync(db, tenantId, dispatcherId, "Dispatcher", PermissionCodes.IncidentsManage, PermissionCodes.ExceptionsResolve, PermissionCodes.DossiersManage);
        await GrantAsync(db, tenantId, fleetUserId, "Wagenparkbeheerder", PermissionCodes.DamageReportsEdit, PermissionCodes.DamageReportsView);
        await GrantAsync(db, tenantId, portalUserId, "Klantportaal", PermissionCodes.ExceptionsResolve);

        var (dossierId, orderId, unloadingStopId) = await DossierWithOrderAsync(db, tenantId, customerId, "Paletten", 10m);
        var (otherDossierId, otherOrderId, _) = await DossierWithOrderAsync(db, tenantId, otherCustomerId, "Kabels", 4m);

        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = Today,
            DriverId = driverId, VehicleId = vehicleId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        return new Harness
        {
            Db = db, TenantId = tenantId, CustomerId = customerId, OtherCustomerId = otherCustomerId,
            DispatcherId = dispatcherId, DriverUserId = driverUserId, FleetUserId = fleetUserId, PortalUserId = portalUserId,
            DriverId = driverId, VehicleId = vehicleId, DossierId = dossierId, OrderId = orderId, UnloadingStopId = unloadingStopId,
            TripId = tripId, OtherDossierId = otherDossierId, OtherOrderId = otherOrderId,
        };
    }

    private static User User(Guid id, Guid tenantId, string email, string first, string last, Guid? employeeId = null) => new()
    {
        Id = id, TenantId = tenantId, Email = email, PasswordHash = "x", FirstName = first, LastName = last,
        EmployeeId = employeeId, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    };

    private static async Task GrantAsync(SqliteTestDbContext db, Guid tenantId, Guid userId, string roleName, params string[] codes)
    {
        var roleId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = roleName, IsActive = true });
        foreach (var code in codes)
        {
            var permission = await db.Context.Permissions.FirstOrDefaultAsync(p => p.Code == code)
                             ?? db.Context.Permissions.Local.FirstOrDefault(p => p.Code == code);
            if (permission is null)
            {
                permission = new Permission { Id = Guid.NewGuid(), Code = code, Module = "test", Action = code };
                db.Context.Permissions.Add(permission);
            }

            db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id });
        }

        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await db.Context.SaveChangesAsync();
    }

    /// <summary>A dossier whose transport activity owns one order (loading + unloading stop), as the UI creates it.</summary>
    private static async Task<(Guid DossierId, Guid OrderId, Guid UnloadingStopId)> DossierWithOrderAsync(
        SqliteTestDbContext db, Guid tenantId, Guid customerId, string goods, decimal quantity)
    {
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var dossiers = new DossierService(db.Context, tenant, audit, new TestClock(Now));
        var activities = new DossierActivityService(db.Context, tenant, audit, dossiers,
            new TransportOrderService(db.Context, tenant, audit, new TestClock(Now)), new TestClock(Now));
        var dossier = await dossiers.CreateAsync(new SaveDossierRequest(CustomerId: customerId), CancellationToken.None);
        var typeId = (await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantId && t.Code == "DIRECT_TRANSPORT")).Id;
        dossier = (await activities.AddAsync(dossier.Id, new SaveDossierActivityRequest(typeId, CreateLinkedOrder: true, Version: dossier.Version), CancellationToken.None))!;
        var orderId = dossier.Activities!.Last().LinkedTransportOrderId!.Value;
        var order = await db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
        order.GoodsDescription = goods;
        order.Quantity = quantity;
        order.QuantityUnit = "pallets";
        order.Status = TransportOrderStatus.InProgress;
        var unloadingId = Guid.NewGuid();
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading,
                LocationName = "Depot", City = "Antwerpen", CountryCode = "BE",
            },
            new TransportOrderStop
            {
                Id = unloadingId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading,
                LocationName = "Haven", City = "Gent", CountryCode = "BE", ContactName = "Mevr. Ontvangst", Gate = "Poort 3",
                TimeRequirement = StopTimeRequirementKind.Before, TimeRequirementTo = new TimeOnly(10, 0),
                RequestedFrom = Now.UtcDateTime.Date.AddHours(8), RequestedTo = Now.UtcDateTime.Date.AddHours(10),
            });
        await db.Context.SaveChangesAsync();
        return (dossier.Id, orderId, unloadingId);
    }

    private static async Task CompleteDeliveryAsync(Harness h, StopExecutionStatus status = StopExecutionStatus.Completed)
    {
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == h.OrderId);
        order.Status = TransportOrderStatus.Completed;
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId, TransportOrderStopId = h.UnloadingStopId,
            Status = status, StatusReason = status == StopExecutionStatus.Completed ? null : "Klant afwezig",
            CompletedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    private static async Task<IncidentDetailDto> DriverIncidentAsync(Harness h, string type = "Damage", string severity = "Medium") =>
        (await h.DriverIncidents().CreateMineAsync(new CreateDriverIncidentRequest(
            "Pallet omgevallen", "Bij het lossen is een pallet omgevallen, folie gescheurd.", type, severity,
            TripId: h.TripId, VehicleId: h.VehicleId, TransportOrderId: h.OrderId), CancellationToken.None))!;

    private static void AssertRelations(Harness h, IncidentDetailDto incident)
    {
        Assert.Equal(h.TripId, incident.TripId);
        Assert.Equal(h.DriverId, incident.DriverId);
        Assert.Equal(h.VehicleId, incident.VehicleId);
        Assert.Equal(h.OrderId, incident.TransportOrderId);
        Assert.Equal(h.DossierId, incident.DossierId);
        Assert.Equal(h.CustomerId, incident.CustomerId);
    }

    // ------------------------------------------------------------------ EX-01

    [Fact]
    public async Task EX01_DriverIncident_DerivesDossierAndCustomer_AndEveryOfficeEditKeepsTheRelations()
    {
        using var h = await SeedAsync();
        var created = await DriverIncidentAsync(h);
        AssertRelations(h, created);
        Assert.Equal("Driver", created.Source);
        Assert.True(created.ExecutionLinksLocked);

        var sut = h.Incidents();
        var report = new IncidentReportSection(created.Title, created.Description, created.IncidentType, "Critical");

        // Severity only, then responsible only, then notes/impact only, then responsibility, then customer message, then status.
        AssertRelations(h, (await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(Report: report), CancellationToken.None))!);
        AssertRelations(h, (await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(Report: report with { ResponsibleUserId = h.DispatcherId }), CancellationToken.None))!);
        AssertRelations(h, (await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(Impact: new IncidentImpactSection(OperationalImpact: "Herlevering nodig")), CancellationToken.None))!);
        AssertRelations(h, (await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(Responsibility: new IncidentResponsibilitySection("Own", "Laadfout")), CancellationToken.None))!);
        AssertRelations(h, (await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(Communication: new IncidentCommunicationSection("Er werd schade vastgesteld.")), CancellationToken.None))!);
        AssertRelations(h, (await sut.ChangeStatusAsync(created.Id, new ChangeIncidentStatusRequest("InProgress"), CancellationToken.None))!);

        // A legacy flat body (no sections) is a no-op, never an erasure.
        var noop = await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(), CancellationToken.None);
        AssertRelations(h, noop!);
        Assert.Equal("Critical", noop!.Severity);
        Assert.Equal("Dina Dispatch", noop.ResponsibleName);

        // The links card without trip/driver keeps them; it can never move them.
        var links = await sut.UpdateAsync(created.Id, new UpdateIncidentRequest(
            Links: new IncidentLinksSection(h.CustomerId, h.DossierId, h.OrderId, h.VehicleId)), CancellationToken.None);
        AssertRelations(h, links!);
        var move = await Assert.ThrowsAsync<DomainValidationException>(() => sut.UpdateAsync(created.Id, new UpdateIncidentRequest(
            Links: new IncidentLinksSection(h.CustomerId, h.DossierId, h.OrderId, h.VehicleId, TripId: Guid.NewGuid())), CancellationToken.None));
        Assert.Contains("tripId", move.FieldErrors!.Keys);

        // The driver still finds it under "Mijn incidenten".
        var mine = await h.DriverIncidents().ListMineAsync(CancellationToken.None);
        Assert.Contains(mine, i => i.Id == created.Id);

        // The update history only names what changed.
        var updates = await h.Db.Context.AuditLogs.Where(a => a.EntityId == created.Id.ToString() && a.Action == "Updated").ToListAsync();
        Assert.Contains(updates, a => a.NewValuesJson!.Contains("\"Severity\":\"Critical\"") && !a.NewValuesJson.Contains("TripId"));
    }

    [Fact]
    public async Task EX01_OfficeIncident_DerivesTripAndDriver_AndRefusesImpossibleCombinations()
    {
        using var h = await SeedAsync();
        var sut = h.Incidents();
        var created = await sut.CreateAsync(new SaveIncidentRequest(
            "Klacht levering", "Klant meldt beschadigde folie.", "CustomerComplaint", "Medium", TransportOrderId: h.OrderId), CancellationToken.None);
        AssertRelations(h, created); // order → customer, dossier, the single trip → driver, vehicle
        Assert.Equal("Office", created.Source);

        var foreignCustomer = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(new SaveIncidentRequest(
            "X", "Y", "Delay", "Low", CustomerId: h.OtherCustomerId, TransportOrderId: h.OrderId), CancellationToken.None));
        Assert.Contains("customerId", foreignCustomer.FieldErrors!.Keys);

        var foreignDossier = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(new SaveIncidentRequest(
            "X", "Y", "Delay", "Low", DossierId: h.OtherDossierId, TransportOrderId: h.OrderId), CancellationToken.None));
        Assert.Contains("dossierId", foreignDossier.FieldErrors!.Keys);

        var tripWithoutOrder = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(new SaveIncidentRequest(
            "X", "Y", "Delay", "Low", TransportOrderId: h.OtherOrderId, TripId: h.TripId), CancellationToken.None));
        Assert.Contains("tripId", tripWithoutOrder.FieldErrors!.Keys);
    }

    // ------------------------------------------------------------------ EX-02

    [Fact]
    public async Task EX02_DamageAndFailedDelivery_MailsAreFullyRendered_StaffGetsTheReport_CustomerCopyIsSafeAndHeld()
    {
        using var h = await SeedAsync();
        var incident = await DriverIncidentAsync(h);

        var failed = new FailedDeliveryService(h.Db.Context, new DevTenantContext(h.TenantId), h.Incidents(),
            new AuditService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(h.DriverUserId)),
            new TestClock(Now), notificationEvents: h.Events());
        await failed.HandleStopFailureAsync(h.TripId, h.UnloadingStopId, "Klant niet aanwezig, poort dicht", CancellationToken.None);

        var rows = await h.Db.Context.OutboxMessages.AsNoTracking()
            .Where(m => m.Kind == MessageKinds.OrderDamageRegistered || m.Kind == MessageKinds.OrderFailedDelivery)
            .ToListAsync();
        Assert.Equal(4, rows.Count); // per event: the customer copy + the staff copy (exceptions.resolve holder)
        Assert.All(rows, m => Assert.Empty(MessageTemplateRenderer.Unresolved(m.Subject, m.Body)));

        var customerDamage = rows.Single(m => m.Kind == MessageKinds.OrderDamageRegistered && m.OwnerType == MessageOwnerType.Customer);
        Assert.Equal(OutboxStatus.AwaitingReview, customerDamage.Status);
        Assert.Contains("schade vastgesteld", customerDamage.Body);
        Assert.DoesNotContain("folie gescheurd", customerDamage.Body); // the driver's raw report is internal

        var staffDamage = rows.Single(m => m.Kind == MessageKinds.OrderDamageRegistered && m.OwnerType == MessageOwnerType.Employee);
        Assert.Equal(OutboxStatus.Pending, staffDamage.Status);
        Assert.Contains("folie gescheurd", staffDamage.Body);

        var customerFailed = rows.Single(m => m.Kind == MessageKinds.OrderFailedDelivery && m.OwnerType == MessageOwnerType.Customer);
        Assert.Equal(OutboxStatus.AwaitingReview, customerFailed.Status);
        Assert.DoesNotContain("poort dicht", customerFailed.Body);
        Assert.Contains("poort dicht", rows.Single(m => m.Kind == MessageKinds.OrderFailedDelivery && m.OwnerType == MessageOwnerType.Employee).Body);
        _ = incident;
    }

    [Fact]
    public async Task EX02_EveryCatalogKind_RendersWithItsTokens_AndABrokenTemplateIsNeverQueuedOrSent()
    {
        using var h = await SeedAsync();
        var outbox = new MessageOutboxService(h.Db.Context, new DevTenantContext(h.TenantId), new TestClock(Now));
        foreach (var info in NotificationEventCatalog.All)
        {
            foreach (var language in new[] { "nl", "fr", "en" })
            {
                var tokens = info.AllowedTokens.ToDictionary(t => t, t => $"<{t}>");
                var result = await outbox.QueueAsync(new MessageRequest(
                    info.MessageKind, MessageOwnerType.Customer, h.CustomerId, tokens, "Test", Guid.NewGuid().ToString(),
                    $"{info.EventKey}:{language}", OverrideAddress: "x@example.test", OverrideLanguage: language), CancellationToken.None);
                var row = await h.Db.Context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == result.MessageId);
                Assert.True(MessageTemplateRenderer.Unresolved(row.Subject, row.Body).Count == 0,
                    $"{info.EventKey}/{language}: {row.Subject} | {row.Body}");
            }
        }

        // A tenant template with a placeholder nobody fills is suppressed with the reason.
        h.Db.Context.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Kind = MessageKinds.OrderDamageRegistered, Channel = MessageChannel.Email,
            Language = "nl", Subject = "Schade {{orderNumber}}", Body = "Beste, {{unknownToken}}", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var broken = await outbox.QueueAsync(new MessageRequest(
            MessageKinds.OrderDamageRegistered, MessageOwnerType.Customer, h.CustomerId,
            new Dictionary<string, string> { ["orderNumber"] = "ORD-1" }, "Test", "1", "broken", OverrideAddress: "x@example.test",
            RequiresReview: true), CancellationToken.None);
        Assert.Equal(QueueOutcome.Suppressed, broken.Outcome);
        Assert.Contains("{{unknownToken}}", broken.Reason);

        // Last line of defence: a Pending row that still carries a placeholder is suppressed, never sent.
        var pending = new OutboxMessage
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Channel = MessageChannel.Email, Kind = MessageKinds.OrderFailedDelivery,
            OwnerType = MessageOwnerType.Customer, OwnerId = h.CustomerId, RecipientAddress = "x@example.test", Language = "nl",
            Subject = "Levering", Body = "Beste, {{details}}", Status = OutboxStatus.Pending, IdempotencyKey = "pending-broken",
        };
        h.Db.Context.OutboxMessages.Add(pending);
        await h.Db.Context.SaveChangesAsync();
        await h.Db.Context.OutboxMessages.Where(m => m.Id != pending.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, OutboxStatus.Sent));
        var sender = new RecordingEmailProvider();
        var dispatcher = new MessageDispatcher(h.Db.Context, sender, sender, new TestClock(Now));
        await dispatcher.DispatchPendingAsync(10, CancellationToken.None);
        Assert.Empty(sender.Sent.Where(m => m.Id == pending.Id));
        Assert.Equal(OutboxStatus.Suppressed, (await h.Db.Context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == pending.Id)).Status);
    }

    // ------------------------------------------------------------------ EX-03

    [Fact]
    public async Task EX03_CriticalVehicleIncident_TellsTheFleetOnce_AndTheLinkedDamageReportCarriesTheSource()
    {
        using var h = await SeedAsync();
        var incident = await DriverIncidentAsync(h, type: "VehicleBreakdown", severity: "Critical");
        Assert.True(incident.VehicleSafetyIssue);
        Assert.True(incident.CanCreateDamageReport);
        Assert.Equal(1, await h.NotificationsOf(h.FleetUserId, "vehicle_safety_issue"));

        // An office edit of the same incident never notifies again.
        await h.Incidents().UpdateAsync(incident.Id, new UpdateIncidentRequest(
            Report: new IncidentReportSection(incident.Title, "Motor valt stil op de E40.", incident.IncidentType, "Critical")), CancellationToken.None);
        Assert.Equal(1, await h.NotificationsOf(h.FleetUserId, "vehicle_safety_issue"));

        var withDamage = await h.FollowUp().CreateDamageReportForIncidentAsync(incident.Id,
            new CreateLinkedDamageReportRequest("Severe", Location: "E40 Aalter"), CancellationToken.None);
        Assert.NotNull(withDamage!.DamageReportId);
        Assert.False(withDamage.CanCreateDamageReport);
        var damage = await h.Db.Context.DamageReports.AsNoTracking().SingleAsync();
        Assert.Equal(incident.Id, damage.SourceIncidentId);
        Assert.Equal(h.VehicleId, damage.VehicleId);
        Assert.Equal(h.TripId, damage.TripId);
        Assert.Equal(h.DriverId, damage.DriverId);
        Assert.Equal(DamageSeverity.Severe, damage.Severity); // existing fleet readiness rule: Severe blocks

        // Idempotent: a second click returns the same report — and writes no second history entry.
        await h.FollowUp().CreateDamageReportForIncidentAsync(incident.Id, new CreateLinkedDamageReportRequest("Moderate"), CancellationToken.None);
        Assert.Equal(1, await h.Db.Context.DamageReports.CountAsync());
        Assert.Equal(1, await h.Db.Context.AuditLogs.CountAsync(a => a.EntityId == incident.Id.ToString() && a.Action == "DamageReportCreated"));

        // The fleet sees the source transport on its own screens, without customer or cost data.
        var list = await new DamageReportService(h.Db.Context, new DevTenantContext(h.TenantId),
            new AuditService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(h.FleetUserId)))
            .ListForVehicleAsync(h.VehicleId, CancellationToken.None);
        Assert.Contains("RIT-0001", list!.Single().SourceLabel);
        Assert.DoesNotContain("Haven BV", list.Single().SourceLabel);
        var issues = await h.FollowUp(h.FleetUserId).VehicleSafetyIssuesAsync(h.VehicleId, CancellationToken.None);
        Assert.Equal(damage.Id, issues.Single().DamageReportId);
    }

    [Fact]
    public async Task EX03_VehicleIssueAfwijking_TellsTheFleet_AndCreatesItsLinkedDamageReport()
    {
        using var h = await SeedAsync();
        var reported = await h.Exceptions(h.DriverUserId).ReportAsync(h.TripId,
            new ReportExceptionRequest(ExecutionExceptionType.VehicleIssue, ExceptionSeverity.High, "Remmen trekken scheef"),
            restrictToOwnDriver: true, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.Success, reported.Outcome);
        Assert.Equal(1, await h.NotificationsOf(h.FleetUserId, "vehicle_safety_issue"));
        Assert.True(reported.Exception!.VehicleSafetyIssue);

        var damageId = await h.FollowUp().CreateDamageReportForExceptionAsync(reported.Exception.Id,
            new CreateLinkedDamageReportRequest("Moderate"), CancellationToken.None);
        var damage = await h.Db.Context.DamageReports.AsNoTracking().SingleAsync(d => d.Id == damageId);
        Assert.Equal(reported.Exception.Id, damage.SourceExceptionId);
        Assert.Equal(h.VehicleId, damage.VehicleId);
        Assert.Equal(h.TripId, damage.TripId);
    }

    // ------------------------------------------------------------------ EX-04 / EX-05

    [Fact]
    public async Task EX04_AnIncidentLinkedOnlyThroughTheOrder_OrOnlyThroughTheTrip_BlocksConfirmation()
    {
        using var h = await SeedAsync();
        await CompleteDeliveryAsync(h);
        var auto = await h.Lifecycle().EvaluateAsync(h.DossierId, CancellationToken.None);
        Assert.True(auto!.CanAutoConfirm);

        // Order only — inserted directly, as legacy rows and other producers may have them.
        h.Db.Context.Incidents.Add(new Modules.Incidents.Entities.Incident
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Order-only", Description = "x",
            IncidentType = Modules.Incidents.Entities.IncidentType.Delay, TransportOrderId = h.OrderId,
        });
        await h.Db.Context.SaveChangesAsync();
        var evaluation = await h.Lifecycle().EvaluateAsync(h.DossierId, CancellationToken.None);
        Assert.False(evaluation!.CanConfirmManually);
        Assert.False(evaluation.CanAutoConfirm);
        Assert.Equal(1, evaluation.Summary.OpenIncidents);
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(h.DossierId, "test", CancellationToken.None));

        // Trip only counts too; an incident of ANOTHER dossier's order does not.
        foreach (var incident in h.Db.Context.Incidents) incident.Status = Modules.Incidents.Entities.IncidentStatus.Resolved;
        h.Db.Context.Incidents.AddRange(
            new Modules.Incidents.Entities.Incident
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Trip-only", Description = "x",
                IncidentType = Modules.Incidents.Entities.IncidentType.Delay, TripId = h.TripId,
            },
            new Modules.Incidents.Entities.Incident
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Other dossier", Description = "x",
                IncidentType = Modules.Incidents.Entities.IncidentType.Delay, TransportOrderId = h.OtherOrderId,
            });
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(1, (await h.Lifecycle().EvaluateAsync(h.DossierId, CancellationToken.None))!.Summary.OpenIncidents);

        // The dossier's incident list shows them as well (one scope everywhere).
        var listed = await h.Incidents().ListAsync(null, null, null, h.DossierId, null, CancellationToken.None);
        Assert.Equal(["Trip-only", "Order-only"], listed.Select(i => i.Title).ToArray());
    }

    [Theory]
    [InlineData(ExceptionSeverity.Low, "Info", true)]
    [InlineData(ExceptionSeverity.Medium, "Warning", true)]
    [InlineData(ExceptionSeverity.High, "Hold", false)]
    [InlineData(ExceptionSeverity.Critical, "Hold", false)]
    public async Task EX05_OpenAfwijking_FollowsTheCentralSeverityPolicy(ExceptionSeverity severity, string checkSeverity, bool autoConfirms)
    {
        using var h = await SeedAsync();
        await CompleteDeliveryAsync(h);
        var reported = await h.Exceptions().ReportAsync(h.TripId,
            new ReportExceptionRequest(ExecutionExceptionType.DamagedPackage, severity, "Hoek van pallet geplet", TransportOrderStopId: h.UnloadingStopId),
            restrictToOwnDriver: false, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.Success, reported.Outcome);

        var evaluation = await h.Lifecycle().EvaluateAsync(h.DossierId, CancellationToken.None);
        var check = Assert.Single(evaluation!.Warnings, w => w.Code.StartsWith("exception.open.", StringComparison.Ordinal));
        Assert.Equal(checkSeverity, check.Severity);
        Assert.Equal(reported.Exception!.Id, check.ProblemId);
        Assert.True(evaluation.CanConfirmManually); // product decision: manual stays possible
        Assert.Equal(autoConfirms, evaluation.CanAutoConfirm);

        if (checkSeverity == "Hold")
        {
            var noReason = await Assert.ThrowsAsync<DomainValidationException>(() => h.Lifecycle().ConfirmAsync(
                h.DossierId, new ConfirmDossierRequest(null, AcknowledgeWarnings: true), CancellationToken.None));
            Assert.Contains("reason", noReason.FieldErrors!.Keys);
            var confirmed = await h.Lifecycle().ConfirmAsync(
                h.DossierId, new ConfirmDossierRequest("Klant akkoord, schade apart opgevolgd", AcknowledgeWarnings: true), CancellationToken.None);
            Assert.Equal("Closed", confirmed!.Status);
        }
        else
        {
            Assert.True(await h.Lifecycle().TryAutoConfirmAsync(h.DossierId, "trip:RIT-0001", CancellationToken.None));
            var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityId == h.DossierId.ToString() && a.Action == DossierLifecycleService.ActionConfirmedAutomatically);
            Assert.Contains("Open afwijking", audit.NewValuesJson); // the attention stays on record
        }
    }

    [Fact]
    public async Task EX05_ProblemAfterConfirmation_NeverUnconfirms_ButIsRecordedOnTheDossier()
    {
        using var h = await SeedAsync();
        await CompleteDeliveryAsync(h);
        Assert.True(await h.Lifecycle().TryAutoConfirmAsync(h.DossierId, "test", CancellationToken.None));

        var incident = await h.Incidents().CreateAsync(new SaveIncidentRequest(
            "Klacht na levering", "Klant meldt ontbrekende doos.", "MissingGoods", "High", TransportOrderId: h.OrderId), CancellationToken.None);

        Assert.Equal(DossierStatus.Closed, (await h.Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == h.DossierId)).Status);
        Assert.Single(incident.ConfirmedDossierNumbers);
        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityId == h.DossierId.ToString() && a.Action == ProblemSignals.AuditProblemAfterConfirmation);
        Assert.Contains("Klacht na levering", audit.NewValuesJson);
        Assert.Equal(1, await h.NotificationsOf(h.DispatcherId, "problem_after_confirmation"));
    }

    // ------------------------------------------------------------------ EX-06 / EX-07

    [Fact]
    public async Task EX06_TheChargeDecisionNeedsTheDecisionRight_NotIncidentManagement()
    {
        using var h = await SeedAsync();
        var incident = await h.Incidents().CreateAsync(new SaveIncidentRequest(
            "Wachttijd", "Klant liet 2u wachten.", "Delay", "Low", TransportOrderId: h.OrderId, ResponsibleParty: "Customer"), CancellationToken.None);
        await h.Incidents().ProposeChargeAsync(incident.Id, new ProposeIncidentChargeRequest(90m, "Wachttijd"), CancellationToken.None);

        // Boekhouding: problems.approve_charge only.
        h.Permissions.Codes.Add(PermissionCodes.ProblemsApproveCharge);
        var decided = await h.Incidents().DecideChargeAsync(incident.Id, new DecideIncidentChargeRequest(true), CancellationToken.None);
        Assert.Equal("Approved", decided!.ChargeDecision);

        var attribute = typeof(Modules.Incidents.Controllers.IncidentsController).GetMethod("DecideCharge")!
            .GetCustomAttributes(typeof(Modules.Identity.Authorization.RequirePermissionAttribute), false)
            .Cast<Modules.Identity.Authorization.RequirePermissionAttribute>().Single();
        Assert.Equal([PermissionCodes.ProblemsApproveCharge], attribute.PermissionCodes);
    }

    [Theory]
    [InlineData(typeof(Modules.Incidents.Controllers.IncidentsController), "DecideCharge", new[] { PermissionCodes.ProblemsApproveCharge })]
    [InlineData(typeof(Modules.Incidents.Controllers.IncidentsController), "AssignableUsers", new[] { PermissionCodes.IncidentsManage, PermissionCodes.ExceptionsResolve })]
    [InlineData(typeof(Modules.Incidents.Controllers.IncidentsController), "CreateRedelivery", new[] { PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage, PermissionCodes.ProblemsCreateRedelivery })]
    [InlineData(typeof(Modules.Incidents.Controllers.IncidentsController), "CreateDamageReport", new[] { PermissionCodes.IncidentsManage })]
    [InlineData(typeof(Modules.Incidents.Controllers.IncidentsController), "History", new[] { PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage })]
    [InlineData(typeof(Modules.Incidents.Controllers.IncidentsController), "VehicleSafetyIssues", new[] { PermissionCodes.DamageReportsView, PermissionCodes.IncidentsView })]
    [InlineData(typeof(Modules.Exceptions.Controllers.ExceptionsController), "CreateDamageReport", new[] { PermissionCodes.ExceptionsResolve })]
    [InlineData(typeof(Modules.Exceptions.Controllers.ExceptionsController), "Escalate", new[] { PermissionCodes.IncidentsManage })]
    public void Endpoints_UseTheirSpecificPermission(Type controller, string action, string[] expected)
    {
        // EX-06/07 + fleet: UI and API agree on narrow rights; the linked-damage commands return the full
        // problem, so they stay with problem handlers (the fleet reports on the vehicle itself).
        var attribute = controller.GetMethod(action)!
            .GetCustomAttributes(typeof(Modules.Identity.Authorization.RequirePermissionAttribute), false)
            .Cast<Modules.Identity.Authorization.RequirePermissionAttribute>().Single();
        Assert.Equal(expected, attribute.PermissionCodes);
    }

    [Fact]
    public void RoleTemplates_DispatcherRedeliversWithoutGeneralOrderRights()
    {
        var dispatcher = DefaultRoleDefinitions.All.Single(r => r.Code == "dispatcher").PermissionCodes;
        Assert.Contains(PermissionCodes.ProblemsCreateRedelivery, dispatcher);
        Assert.DoesNotContain(PermissionCodes.OrdersCreate, dispatcher);
        Assert.DoesNotContain(PermissionCodes.UsersView, dispatcher);
        var boekhouding = DefaultRoleDefinitions.All.Single(r => r.Code == "boekhouding").PermissionCodes;
        Assert.Contains(PermissionCodes.ProblemsApproveCharge, boekhouding);
        Assert.DoesNotContain(PermissionCodes.IncidentsManage, boekhouding);
        Assert.Contains(DefaultRoleUpgrades.Steps, s => s.Version == 40
            && s.GrantsByTemplateCode["dispatcher"].Contains(PermissionCodes.ProblemsCreateRedelivery));
    }

    [Fact]
    public async Task EX07_AssignableUsers_AreInternalProblemHandlers_WithSafeFieldsOnly()
    {
        using var h = await SeedAsync();
        var users = await h.FollowUp().AssignableUsersAsync(CancellationToken.None);
        var dispatcher = Assert.Single(users);
        Assert.Equal(h.DispatcherId, dispatcher.Id);
        Assert.Equal("Dina Dispatch", dispatcher.DisplayName);
        Assert.Equal("Dispatcher", dispatcher.RoleName);
        // The portal identity holding a legacy internal grant is never offered.
        Assert.DoesNotContain(users, u => u.Id == h.PortalUserId);
        Assert.Equal(3, typeof(AssignableUserDto).GetProperties().Length); // no e-mail / account data

        var refused = await h.Exceptions().AssignAsync(
            (await h.Exceptions().ReportAsync(h.TripId, new ReportExceptionRequest(ExecutionExceptionType.Delay, ExceptionSeverity.Low, "File"),
                false, CancellationToken.None)).Exception!.Id, h.PortalUserId, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.ValidationFailed, refused.Outcome);
    }

    // ------------------------------------------------------------------ EX-08

    [Fact]
    public async Task EX08_AFailedDelivery_IsNotDelivered_ThenRedeliveryPlanned_AndAPartialOneIsPartial()
    {
        using var h = await SeedAsync();
        await CompleteDeliveryAsync(h, StopExecutionStatus.Failed);
        var outcomes = await DeliveryOutcomeRules.ResolveAsync(h.Db.Context, h.TenantId, [h.OrderId], CancellationToken.None);
        Assert.Equal(DeliveryOutcomes.NotDelivered, outcomes[h.OrderId].Outcome);

        var failed = new FailedDeliveryService(h.Db.Context, new DevTenantContext(h.TenantId), h.Incidents(),
            new AuditService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(null)), new TestClock(Now));
        await failed.HandleStopFailureAsync(h.TripId, h.UnloadingStopId, "Klant afwezig", CancellationToken.None);
        var incident = await h.Db.Context.Incidents.AsNoTracking().SingleAsync(i => i.SourceStopId == h.UnloadingStopId);
        Assert.Equal(h.DossierId, incident.DossierId);
        Assert.Equal(h.DriverId, incident.DriverId);
        await h.Incidents().CreateRedeliveryAsync(incident.Id, CancellationToken.None);

        outcomes = await DeliveryOutcomeRules.ResolveAsync(h.Db.Context, h.TenantId, [h.OrderId], CancellationToken.None);
        Assert.Equal(DeliveryOutcomes.RedeliveryPlanned, outcomes[h.OrderId].Outcome);
        Assert.NotNull(outcomes[h.OrderId].RedeliveryOrderNumber);

        using var p = await SeedAsync();
        await CompleteDeliveryAsync(p, StopExecutionStatus.PartiallyCompleted);
        Assert.Equal(DeliveryOutcomes.PartiallyDelivered,
            (await DeliveryOutcomeRules.ResolveAsync(p.Db.Context, p.TenantId, [p.OrderId], CancellationToken.None))[p.OrderId].Outcome);
    }

    // ------------------------------------------------------------------ escalation, redelivery, retention, history

    [Fact]
    public async Task Escalation_CarriesEveryRelation_IsIdempotent_AndBothRecordsStayTraceable()
    {
        using var h = await SeedAsync();
        var reported = await h.Exceptions(h.DriverUserId).ReportAsync(h.TripId,
            new ReportExceptionRequest(ExecutionExceptionType.MissingPackage, ExceptionSeverity.High, "2 dozen ontbreken",
                TransportOrderStopId: h.UnloadingStopId, Quantity: 2m), restrictToOwnDriver: true, CancellationToken.None);
        var exceptionId = reported.Exception!.Id;
        await h.Exceptions().UpdateCustomerMessageAsync(exceptionId, "Twee dozen werden niet geleverd; we leveren ze na.", CancellationToken.None);

        var incident = await h.FollowUp().EscalateExceptionAsync(exceptionId, CancellationToken.None);
        AssertRelations(h, incident!);
        Assert.Equal("Exception", incident!.Source);
        Assert.Equal(exceptionId, incident.SourceExceptionId);
        Assert.Equal("MissingGoods", incident.IncidentType);
        Assert.Equal("High", incident.Severity);
        Assert.Equal("Twee dozen werden niet geleverd; we leveren ze na.", incident.CustomerMessage);

        var again = await h.FollowUp().EscalateExceptionAsync(exceptionId, CancellationToken.None);
        Assert.Equal(incident.Id, again!.Id);
        var detail = await h.Exceptions().GetByIdAsync(exceptionId, CancellationToken.None);
        Assert.Equal(incident.Id, detail!.EscalatedIncidentId);
        Assert.Equal(ExecutionExceptionStatus.Investigating, detail.Status);

        // Redelivery of the not-delivered quantity, with the stop's time requirement and backlinks.
        var withRedelivery = await h.Incidents().CreateRedeliveryAsync(incident.Id, CancellationToken.None);
        var redelivery = await h.Db.Context.TransportOrders.AsNoTracking().Include(o => o.Stops)
            .SingleAsync(o => o.Id == withRedelivery!.LinkedRedeliveryOrderId);
        Assert.Equal(2m, redelivery.Quantity);
        Assert.Equal(h.OrderId, redelivery.RedeliveryOfOrderId);
        Assert.Equal(incident.Id, redelivery.RedeliverySourceIncidentId);
        Assert.Null(redelivery.AgreedPrice); // unpriced: never a silent copy of the original billing
        Assert.False(redelivery.FreeConfirmed);
        var drop = redelivery.Stops.Single(s => s.StopType == StopType.Unloading);
        Assert.Equal(StopTimeRequirementKind.Before, drop.TimeRequirement);
        Assert.Equal(new TimeOnly(10, 0), drop.TimeRequirementTo);
        Assert.Equal("Poort 3", drop.Gate);
        Assert.NotNull(drop.RequestedFrom);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "TransportOrder" && a.EntityId == redelivery.Id.ToString() && a.Action == "RedeliveryCreated"));
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "TransportOrder" && a.EntityId == h.OrderId.ToString() && a.Action == "RedeliveryRequested"));
        Assert.True(await h.Db.Context.DossierOrders.AnyAsync(l => l.DossierId == h.DossierId && l.TransportOrderId == redelivery.Id));

        var second = await h.Incidents().CreateAsync(new SaveIncidentRequest("X", "Y", "MissingGoods", "Low", TransportOrderId: h.OrderId), CancellationToken.None);
        var tooMuch = await Assert.ThrowsAsync<DomainValidationException>(() => h.Incidents().CreateRedeliveryAsync(
            second.Id, CancellationToken.None, new CreateRedeliveryRequest(Quantity: 11m)));
        Assert.Contains("quantity", tooMuch.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Photos_OfAClosedAfwijking_AreRetainedEvidence()
    {
        using var h = await SeedAsync();
        var exceptions = h.Exceptions();
        var reported = await exceptions.ReportAsync(h.TripId,
            new ReportExceptionRequest(ExecutionExceptionType.DamagedPackage, ExceptionSeverity.Medium, "Folie gescheurd"), false, CancellationToken.None);
        var withPhoto = await exceptions.AttachPhotoAsync(reported.Exception!.Id, "schade.jpg", "image/jpeg",
            new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0]), restrictToOwnDriver: false, CancellationToken.None);
        var photoId = withPhoto.Exception!.Photos.Single().Id;
        await exceptions.ChangeStatusAsync(reported.Exception.Id, new ChangeExceptionStatusRequest(ExecutionExceptionStatus.Resolved, "Klant gecrediteerd"), CancellationToken.None);

        var refused = await exceptions.DeletePhotoAsync(reported.Exception.Id, photoId, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.InvalidState, refused.Outcome);
        Assert.Single((await exceptions.GetByIdAsync(reported.Exception.Id, CancellationToken.None))!.Photos);
    }

    [Fact]
    public async Task DomainHistory_ShowsWhoAndWhat_ButRedactsFinancialsForOperationalRoles()
    {
        using var h = await SeedAsync();
        var incident = await h.Incidents().CreateAsync(new SaveIncidentRequest(
            "Wachttijd", "Klant liet wachten.", "Delay", "Low", TransportOrderId: h.OrderId, ResponsibleParty: "Customer"), CancellationToken.None);
        await h.Incidents().ProposeChargeAsync(incident.Id, new ProposeIncidentChargeRequest(90m, "Wachttijd 2u"), CancellationToken.None);
        await h.Incidents().ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("Resolved", "Afgesproken met klant"), CancellationToken.None);

        var resolved = await h.Incidents().GetAsync(incident.Id, CancellationToken.None);
        Assert.Equal("Dina Dispatch", resolved!.ResolvedByName);

        var history = await h.FollowUp().HistoryAsync("Incident", incident.Id, 1, 25, CancellationToken.None);
        Assert.Contains(history!.Items, i => i.Action == "ChargeProposed" && i.UserName == "Dina Dispatch");
        Assert.DoesNotContain(history.Items, i => (i.NewValuesJson ?? string.Empty).Contains("ChargeAmount"));

        h.Permissions.Codes.Add(PermissionCodes.ProblemsApproveCharge);
        var financial = await h.FollowUp().HistoryAsync("Incident", incident.Id, 1, 25, CancellationToken.None);
        Assert.Contains(financial!.Items, i => (i.NewValuesJson ?? string.Empty).Contains("ChargeAmount"));
    }

    private sealed class RecordingEmailProvider : IEmailProvider, ISmsProvider
    {
        public List<OutboxMessage> Sent { get; } = [];

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
