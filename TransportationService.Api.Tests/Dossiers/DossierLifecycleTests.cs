using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Dossier confirmation sprint 2026-09-23 — one lifecycle service for manual confirmation,
/// automatic confirmation after operational completion, reopening and cancelling. Closed IS
/// the confirmed state (backward compatible); the new metadata says how and by whom.
/// </summary>
public class DossierLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        public DevTenantContext Tenant => new(TenantId);

        public DossierLifecycleService Lifecycle(Guid? userId = null, Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var currentUser = new DevCurrentUserContext(userId ?? UserId);
            var audit = new AuditService(Db.Context, tenant, currentUser);
            var dossiers = new DossierService(Db.Context, tenant, audit, new TestClock(Now));
            return new DossierLifecycleService(Db.Context, tenant, audit, new TestClock(Now), currentUser, () => dossiers, new DossierReadinessService(Db.Context, tenant));
        }

        public DossierService Dossiers()
        {
            var tenant = Tenant;
            return new DossierService(Db.Context, tenant, new AuditService(Db.Context, tenant, new DevCurrentUserContext(UserId)), new TestClock(Now));
        }

        public DossierActivityService Activities()
        {
            var tenant = Tenant;
            var audit = new AuditService(Db.Context, tenant, new DevCurrentUserContext(UserId));
            var dossiers = new DossierService(Db.Context, tenant, audit, new TestClock(Now));
            var orders = new TransportOrderService(Db.Context, tenant, audit, new TestClock(Now));
            return new DossierActivityService(Db.Context, tenant, audit, dossiers, orders, new TestClock(Now));
        }

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        public Task<TransportDossier> Entity(Guid id) =>
            Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == id);

        public Task<List<Modules.Auditing.Entities.AuditLog>> Audits(Guid id, string action) =>
            Db.Context.AuditLogs.AsNoTracking().Where(a => a.EntityType == "TransportDossier" && a.EntityId == id.ToString() && a.Action == action).ToListAsync();

        /// <summary>Dossier + N transport activities each with its own linked order in the given statuses.</summary>
        public async Task<(DossierDetailDto Dossier, List<Guid> OrderIds)> DossierWithOrdersAsync(params TransportOrderStatus[] statuses)
        {
            var dossier = await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId), CancellationToken.None);
            var orderIds = new List<Guid>();
            foreach (var status in statuses)
            {
                dossier = (await Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
                    await TypeIdAsync("DIRECT_TRANSPORT"), CreateLinkedOrder: true, Version: dossier.Version), CancellationToken.None))!;
                var orderId = dossier.Activities!.Last().LinkedTransportOrderId!.Value;
                var order = await Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
                order.Status = status;
                await Db.Context.SaveChangesAsync();
                orderIds.Add(orderId);
            }

            return (dossier, orderIds);
        }

        public async Task AddStandaloneActivityAsync(Guid dossierId, string typeCode, DateOnly? plannedDate)
        {
            var dossier = await Dossiers().GetAsync(dossierId, CancellationToken.None);
            await Activities().AddAsync(dossierId, new SaveDossierActivityRequest(
                await TypeIdAsync(typeCode), PlannedDate: plannedDate, Version: dossier!.Version), CancellationToken.None);
        }

        public async Task AddFailedStopExecutionAsync(Guid orderId)
        {
            var stopId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            Db.Context.TransportOrderStops.Add(new TransportOrderStop
            {
                Id = stopId, TenantId = TenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Unloading,
                LocationName = "Losplaats", City = "Gent",
            });
            Db.Context.Trips.Add(new Trip { Id = tripId, TenantId = TenantId, TripNumber = "RIT-X", TripDate = new DateOnly(2026, 9, 22), Status = TripStatus.Completed });
            Db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = TenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
            Db.Context.StopExecutions.Add(new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TripId = tripId, TransportOrderStopId = stopId,
                Status = StopExecutionStatus.Failed, StatusReason = "Klant afwezig", CompletedAt = Now.UtcDateTime,
            });
            await Db.Context.SaveChangesAsync();
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme BV", IsActive = true, IsDefault = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV", IsActive = true, DefaultLegalEntityId = entityId });
        db.Context.Users.Add(new Modules.Identity.Entities.User { Id = UserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId);
    }

    private static ConfirmDossierRequest Ack(string? reason = null) => new(reason, AcknowledgeWarnings: true);

    // ------------------------------------------------------------ manual confirmation

    [Fact]
    public async Task ManualConfirmation_SetsMetadata_AndAuditsOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);

        var confirmed = await h.Lifecycle().ConfirmAsync(dossier.Id, Ack("Alles afgehandeld"), CancellationToken.None);

        Assert.Equal("Closed", confirmed!.Status);
        Assert.Equal("Manual", confirmed.ConfirmationSource);
        Assert.Equal(Now.UtcDateTime, confirmed.ConfirmedAt);
        Assert.Equal(UserId, confirmed.ConfirmedByUserId);
        Assert.Equal("Piet Planner", confirmed.ConfirmedByName);
        Assert.Equal("Alles afgehandeld", confirmed.ConfirmationReason);
        var entity = await h.Entity(dossier.Id);
        Assert.Equal(DossierStatus.Closed, entity.Status);
        Assert.Equal(DossierConfirmationSource.Manual, entity.ConfirmationSource);
        Assert.Equal(Now.UtcDateTime, entity.ClosedAt);
        var audit = Assert.Single(await h.Audits(dossier.Id, "ConfirmedManually"));
        Assert.Equal(UserId, audit.UserId);
        Assert.Contains("Alles afgehandeld", audit.NewValuesJson);
    }

    [Fact]
    public async Task RepeatedManualConfirmation_IsIdempotent_NoSecondAudit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        await h.Lifecycle().ConfirmAsync(dossier.Id, Ack(), CancellationToken.None);

        var again = await h.Lifecycle().ConfirmAsync(dossier.Id, Ack("nog eens"), CancellationToken.None);

        Assert.Equal("Closed", again!.Status);
        Assert.Null(again.ConfirmationReason);
        Assert.Single(await h.Audits(dossier.Id, "ConfirmedManually"));
    }

    [Fact]
    public async Task ForeignTenant_CannotSeeOrConfirm()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);

        var foreign = h.Lifecycle(tenantId: Guid.NewGuid());
        Assert.Null(await foreign.EvaluateAsync(dossier.Id, CancellationToken.None));
        Assert.Null(await foreign.ConfirmAsync(dossier.Id, Ack(), CancellationToken.None));
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossier.Id)).Status);
    }

    [Fact]
    public async Task HistoricDossier_WithDraftOrder_WarnsButConfirmsWhenAcknowledged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Transport ran yesterday; the planner creates the dossier today with a draft order and no trip.
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Draft);

        var evaluation = await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.True(evaluation!.CanConfirmManually);
        Assert.False(evaluation.CanAutoConfirm);
        Assert.Empty(evaluation.Blockers);
        Assert.Contains(evaluation.Warnings, w => w.Code == "order.not_completed");

        // Without the explicit acknowledgement the warnings are not silently skipped.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().ConfirmAsync(dossier.Id, new ConfirmDossierRequest(null, AcknowledgeWarnings: false), CancellationToken.None));

        var confirmed = await h.Lifecycle().ConfirmAsync(dossier.Id, Ack("Uitgevoerd op 22/09, administratief nagemaakt"), CancellationToken.None);
        Assert.Equal("Closed", confirmed!.Status);
        Assert.Equal("Manual", confirmed.ConfirmationSource);
        // No fictitious trip/order status was invented.
        Assert.Equal(TransportOrderStatus.Draft, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task OpenIncident_IsARealBlocker_ForManualAndAutomaticConfirmation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        h.Db.Context.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = dossier.Id, Title = "Schade", Status = IncidentStatus.New,
        });
        await h.Db.Context.SaveChangesAsync();

        var evaluation = await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.False(evaluation!.CanConfirmManually);
        Assert.False(evaluation.CanAutoConfirm);
        Assert.Contains(evaluation.Blockers, b => b.Code == "incident.open");

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Lifecycle().ConfirmAsync(dossier.Id, Ack(), CancellationToken.None));
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossier.Id)).Status);
    }

    [Fact]
    public async Task VersionMismatch_IsAConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);

        await Assert.ThrowsAsync<DossierVersionConflictException>(() =>
            h.Lifecycle().ConfirmAsync(dossier.Id, new ConfirmDossierRequest(null, true, Version: Guid.NewGuid()), CancellationToken.None));
    }

    // ------------------------------------------------------------ automatic confirmation

    [Fact]
    public async Task OneCompletedOrder_AutoConfirms_WithAutomaticSource()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orderIds) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);

        var confirmed = await h.Lifecycle().TryAutoConfirmForOrdersAsync(orderIds, "trip:RIT-1", CancellationToken.None);

        Assert.Equal(1, confirmed);
        var entity = await h.Entity(dossier.Id);
        Assert.Equal(DossierStatus.Closed, entity.Status);
        Assert.Equal(DossierConfirmationSource.Automatic, entity.ConfirmationSource);
        Assert.Null(entity.ConfirmedByUserId);
        Assert.Equal(Now.UtcDateTime, entity.ClosedAt);
        var audit = Assert.Single(await h.Audits(dossier.Id, "ConfirmedAutomatically"));
        Assert.Contains("trip:RIT-1", audit.NewValuesJson);
    }

    [Fact]
    public async Task TwoOrders_OnlyOneCompleted_StaysOpen_ThenBothCompleted_Confirms()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orderIds) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed, TransportOrderStatus.InProgress);

        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));
        var evaluation = await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.Contains(evaluation!.AutoBlockers, b => b.Code == "order.not_completed" && b.TransportOrderId == orderIds[1]);
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossier.Id)).Status);

        var second = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderIds[1]);
        second.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();

        Assert.True(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));
        Assert.Equal(DossierStatus.Closed, (await h.Entity(dossier.Id)).Status);
    }

    [Fact]
    public async Task OpenExecutableStandaloneActivity_BlocksAutoConfirmation_NonOperationalOneDoesNot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        // Opslag: not planning-relevant → purely commercial, never blocks.
        await h.AddStandaloneActivityAsync(dossier.Id, "OPSLAG", null);
        Assert.True(await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None) is { CanAutoConfirm: true });

        // Plateau: planning-relevant and still planned in the future → open operational part.
        await h.AddStandaloneActivityAsync(dossier.Id, "PLATEAU", new DateOnly(2026, 9, 30));
        var evaluation = await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.False(evaluation!.CanAutoConfirm);
        Assert.Contains(evaluation.AutoBlockers, b => b.Code == "activity.not_executed");
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task ExecutedStandaloneActivity_InThePast_CountsAsDone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        await h.AddStandaloneActivityAsync(dossier.Id, "PLATEAU", new DateOnly(2026, 9, 20));

        Assert.True(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task FailedDelivery_BlocksAutoConfirmation_ButIsOnlyAWarningManually()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orderIds) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        await h.AddFailedStopExecutionAsync(orderIds[0]);

        var evaluation = await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.False(evaluation!.CanAutoConfirm);
        Assert.Contains(evaluation.AutoBlockers, b => b.Code == "delivery.not_completed");
        Assert.True(evaluation.CanConfirmManually);
        Assert.Contains(evaluation.Warnings, w => w.Code == "delivery.not_completed");
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task CancelledOrders_AreIgnored_ButNothingExecuted_NeverAutoConfirms()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (onlyCancelled, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Cancelled);
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(onlyCancelled.Id, "test", CancellationToken.None));
        Assert.Contains((await h.Lifecycle().EvaluateAsync(onlyCancelled.Id, CancellationToken.None))!.AutoBlockers, b => b.Code == "nothing.completed");

        var (mixed, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed, TransportOrderStatus.Cancelled);
        Assert.True(await h.Lifecycle().TryAutoConfirmAsync(mixed.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task CompletionEventTwice_AndAlreadyConfirmed_NeverConfirmsTwice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orderIds) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);

        Assert.Equal(1, await h.Lifecycle().TryAutoConfirmForOrdersAsync(orderIds, "trip:RIT-1", CancellationToken.None));
        Assert.Equal(0, await h.Lifecycle().TryAutoConfirmForOrdersAsync(orderIds, "trip:RIT-1", CancellationToken.None));
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "again", CancellationToken.None));

        Assert.Single(await h.Audits(dossier.Id, "ConfirmedAutomatically"));
        Assert.Empty(await h.Audits(dossier.Id, "ConfirmedManually"));
    }

    // ------------------------------------------------------------ reopen

    [Fact]
    public async Task Reopen_RequiresAReason_KeepsHistory_AndAllowsANewConfirmation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orderIds) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        await h.Lifecycle().TryAutoConfirmForOrdersAsync(orderIds, "trip:RIT-1", CancellationToken.None);
        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice { Id = invoiceId, TenantId = h.TenantId, CustomerId = h.CustomerId, InvoiceNumber = "FAC-1", InvoiceDate = new(2026, 9, 23), DueDate = new(2026, 10, 23), Status = InvoiceStatus.Draft });
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().ReopenAsync(dossier.Id, new ReopenDossierRequest(" "), CancellationToken.None));

        var reopened = await h.Lifecycle().ReopenAsync(dossier.Id, new ReopenDossierRequest("Nalevering nodig"), CancellationToken.None);

        Assert.Equal("Open", reopened!.Status);
        Assert.Null(reopened.ConfirmedAt);
        Assert.Null(reopened.ConfirmationSource);
        var reopenAudit = Assert.Single(await h.Audits(dossier.Id, "Reopened"));
        Assert.Equal(UserId, reopenAudit.UserId);
        Assert.Contains("Nalevering nodig", reopenAudit.NewValuesJson);
        Assert.Contains("Automatic", reopenAudit.OldValuesJson);
        // The earlier confirmation is still in the history; the invoice still exists.
        Assert.Single(await h.Audits(dossier.Id, "ConfirmedAutomatically"));
        Assert.NotNull(await h.Db.Context.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invoiceId));

        // A later completion confirms again — explicitly allowed after a reopen.
        Assert.True(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "trip:RIT-2", CancellationToken.None));
        Assert.Equal(2, (await h.Audits(dossier.Id, "ConfirmedAutomatically")).Count);
    }

    [Fact]
    public async Task Reopen_OfAnOpenDossier_IsRefused_AndForeignTenantSeesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().ReopenAsync(dossier.Id, new ReopenDossierRequest("x"), CancellationToken.None));
        await h.Lifecycle().ConfirmAsync(dossier.Id, Ack(), CancellationToken.None);
        Assert.Null(await h.Lifecycle(tenantId: Guid.NewGuid()).ReopenAsync(dossier.Id, new ReopenDossierRequest("x"), CancellationToken.None));
        Assert.Equal(DossierStatus.Closed, (await h.Entity(dossier.Id)).Status);
    }

    // ------------------------------------------------------------ cancel

    [Fact]
    public async Task Cancel_RequiresReason_AndOnlyWhenNoOrderProgressed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (progressed, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Completed);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().CancelAsync(progressed.Id, new CancelDossierRequest("Klant zegt af"), CancellationToken.None));

        var (cancellable, _) = await h.DossierWithOrdersAsync(TransportOrderStatus.Cancelled);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().CancelAsync(cancellable.Id, new CancelDossierRequest(""), CancellationToken.None));

        var cancelled = await h.Lifecycle().CancelAsync(cancellable.Id, new CancelDossierRequest("Klant zegt af"), CancellationToken.None);

        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.Equal("Klant zegt af", cancelled.CancellationReason);
        Assert.Equal(UserId, (await h.Entity(cancellable.Id)).CancelledByUserId);
        Assert.Single(await h.Audits(cancellable.Id, "Cancelled"));
        // A cancelled dossier is neither confirmable nor auto-confirmable.
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(cancellable.Id, "x", CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Lifecycle().ConfirmAsync(cancellable.Id, Ack(), CancellationToken.None));
        // Reopen brings it back to Open.
        Assert.Equal("Open", (await h.Lifecycle().ReopenAsync(cancellable.Id, new ReopenDossierRequest("Toch uitvoeren"), CancellationToken.None))!.Status);
    }
}
