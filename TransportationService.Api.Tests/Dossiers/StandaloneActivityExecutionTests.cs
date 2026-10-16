using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Services;
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
/// T-H7 / T-H8 / M11 (acceptance 2026-10-16). Standalone activities (crane work on site, plateau)
/// are executable units with their own status; completing/cancelling them re-runs dossier
/// auto-confirmation; executed billable activities are never invisible to accounting; and the
/// dossier list and detail read ONE price-status computation that ignores cancelled work.
/// </summary>
public class StandaloneActivityExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Yesterday = new(2026, 9, 22);
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        private DevTenantContext Tenant => new(TenantId);
        private AuditService Audit => new(Db.Context, Tenant, new DevCurrentUserContext(UserId));

        public DossierService Dossiers() => new(Db.Context, Tenant, Audit, new TestClock(Now));

        public DossierLifecycleService Lifecycle()
        {
            var dossiers = Dossiers();
            return new DossierLifecycleService(Db.Context, Tenant, Audit, new TestClock(Now), new DevCurrentUserContext(UserId),
                () => dossiers, new DossierReadinessService(Db.Context, Tenant));
        }

        public DossierActivityService Activities() =>
            new(Db.Context, Tenant, Audit, Dossiers(), new TransportOrderService(Db.Context, Tenant, Audit, new TestClock(Now)), new TestClock(Now));

        public DossierActivityExecutionService Execution() =>
            new(Db.Context, Tenant, Audit, Dossiers(), Lifecycle(),
                new PlanningConflictService(Db.Context, Tenant, new QualificationStatusCalculator(), new TestClock(Now)),
                new DevCurrentUserContext(UserId), new TestClock(Now));

        public InvoiceControlService Control() => new(Db.Context, Tenant, new TestClock(Now));

        public InvoiceService Invoices()
        {
            var audit = Audit;
            return new InvoiceService(Db.Context, Tenant, audit, new TestClock(Now), new InvoiceNumberService(Db.Context, Tenant),
                new Modules.Partners.Services.CustomerBillingConfigService(Db.Context, Tenant, audit, new TestClock(Now)),
                new Modules.Accounting.Services.AccountingService(Db.Context, Tenant, audit));
        }

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        public Task<TransportDossier> Entity(Guid id) => Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == id);

        public async Task<DossierDetailDto> NewDossierAsync() =>
            await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId), CancellationToken.None);

        public async Task<DossierDetailDto> Current(Guid dossierId) => (await Dossiers().GetAsync(dossierId, CancellationToken.None))!;

        /// <summary>A transport activity with its linked order put in <paramref name="status"/>.</summary>
        public async Task<Guid> AddOrderAsync(Guid dossierId, TransportOrderStatus status, decimal? agreedPrice = null)
        {
            var dossier = (await Activities().AddAsync(dossierId, new SaveDossierActivityRequest(
                await TypeIdAsync("DIRECT_TRANSPORT"), CreateLinkedOrder: true, Version: (await Current(dossierId)).Version), CancellationToken.None))!;
            var orderId = dossier.Activities!.Last().LinkedTransportOrderId!.Value;
            var order = await Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
            order.Status = status;
            if (agreedPrice is { } price)
            {
                order.AgreedPrice = price;
                order.PriceIsManual = true;
            }

            await Db.Context.SaveChangesAsync();
            return orderId;
        }

        public async Task<Guid> AddStandaloneAsync(Guid dossierId, string code, string label, DateOnly? date = null)
        {
            var dossier = (await Activities().AddAsync(dossierId, new SaveDossierActivityRequest(
                await TypeIdAsync(code), Label: label, PlannedDate: date ?? Yesterday, Version: (await Current(dossierId)).Version),
                CancellationToken.None))!;
            return dossier.Activities!.Single(a => a.Label == label).Id;
        }

        public Task<DossierDetailDto?> Complete(Guid dossierId, Guid activityId) =>
            Execution().CompleteAsync(dossierId, activityId, new DossierActivityTransitionRequest(), CancellationToken.None);

        public void Price(Guid activityId, decimal? amount, bool free = false) =>
            Db.Context.DossierActivityPricings.Add(new DossierActivityPricing
            {
                Id = Guid.NewGuid(), TenantId = TenantId, DossierActivityId = activityId,
                PricingSource = free ? ActivityPricingSource.Lines : ActivityPricingSource.OneOff,
                FixedAmount = free ? null : amount, AgreedPrice = free ? 0m : amount, FreeConfirmed = free,
                Status = OrderPricingStatus.Draft,
            });
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
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme BV", IsActive = true, IsDefault = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV", VatNumber = "BE0123456789",
            IsActive = true, DefaultLegalEntityId = entityId, InvoiceGrouping = "PerDossier",
        });
        db.Context.Users.Add(new Modules.Identity.Entities.User { Id = UserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId);
    }

    // ------------------------------------------------------------ T-H7: lifecycle

    [Fact]
    public async Task OrderCraneAndPlateau_StayOpenUntilTheLastOneCompletes_ThenAutoConfirm()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        var orderId = await h.AddOrderAsync(dossier.Id, TransportOrderStatus.InProgress);
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan werf B");
        var plateau = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "Plateau werf B");

        // Mixed order: the plateau first, then the crane — the order is still running.
        Assert.Equal("Open", (await h.Complete(dossier.Id, plateau))!.Status);
        Assert.Equal("Open", (await h.Complete(dossier.Id, crane))!.Status);
        var evaluation = await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.Contains(evaluation!.AutoBlockers, b => b.Code == "order.not_completed");
        Assert.DoesNotContain(evaluation.AutoBlockers, b => b.Code == "activity.not_executed");

        // The order completes last: the ordinary order-completion trigger confirms the dossier.
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
        order.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(1, await h.Lifecycle().TryAutoConfirmForOrdersAsync([orderId], "order:completed", CancellationToken.None));

        var entity = await h.Entity(dossier.Id);
        Assert.Equal((DossierStatus.Closed, DossierConfirmationSource.Automatic), (entity.Status, entity.ConfirmationSource));
    }

    [Fact]
    public async Task CompletingTheLastStandaloneActivity_AutoConfirms_WithTheActivityAsTrigger()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Completed);
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan werf B");
        var plateau = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "Plateau werf B");

        // Completed order + planned crane + planned plateau: nothing confirms on dates alone.
        Assert.False(await h.Lifecycle().TryAutoConfirmAsync(dossier.Id, "test", CancellationToken.None));

        var afterCrane = await h.Complete(dossier.Id, crane);
        Assert.Equal("Open", afterCrane!.Status);
        Assert.Equal("Completed", afterCrane.Activities!.Single(a => a.Id == crane).ExecutionStatus);
        Assert.Equal("Piet Planner", afterCrane.Activities!.Single(a => a.Id == crane).CompletedByName);
        var blockers = (await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None))!.AutoBlockers;
        Assert.Equal(plateau, Assert.Single(blockers, b => b.Code == "activity.not_executed").ActivityId);

        var confirmed = await h.Complete(dossier.Id, plateau);

        Assert.Equal(("Closed", "Automatic"), (confirmed!.Status, confirmed.ConfirmationSource));
        var audit = await h.Db.Context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityId == dossier.Id.ToString() && a.Action == DossierLifecycleService.ActionConfirmedAutomatically);
        Assert.Contains("activity:Plateau werf B", audit.NewValuesJson);

        // The dossier timeline tells it: both activities executed, then the system confirmed.
        var timeline = (await new DossierTimelineService(h.Db.Context, new DevTenantContext(h.TenantId))
            .GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Contains(timeline, e => e.Type == "activity.completed" && e.Title.Contains("Kraan werf B") && e.Title.EndsWith("uitgevoerd"));
        Assert.Contains(timeline, e => e.Type == "activity.completed" && e.Title.Contains("Plateau werf B"));
        var system = Assert.Single(timeline, e => e.Type == "dossier.confirmed_automatically");
        Assert.Equal("System", system.ActorKind);
        Assert.Contains("na afhandeling van activiteit Plateau werf B", system.Title);
        Assert.DoesNotContain(timeline, e => e.Type == "dossier.changed");
    }

    [Fact]
    public async Task ACustomerRequestAwaitingReview_BlocksEvenManualConfirmation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Submitted);

        var evaluation = (await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None))!;
        Assert.Contains(evaluation.Blockers, b => b.Code == "order.awaiting_review");
        Assert.False(evaluation.CanConfirmManually);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Lifecycle().ConfirmAsync(
            dossier.Id, new ConfirmDossierRequest("historisch", AcknowledgeWarnings: true), CancellationToken.None));
    }

    [Fact]
    public async Task ACancelledStandaloneActivity_CountsAsResolved_LikeACancelledOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Completed);
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan");
        var plateau = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "Plateau");
        await h.Complete(dossier.Id, crane);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Execution().CancelAsync(dossier.Id, plateau, new CancelDossierActivityRequest("  "), CancellationToken.None));

        var cancelled = await h.Execution().CancelAsync(dossier.Id, plateau, new CancelDossierActivityRequest("Klant had geen plateau nodig"), CancellationToken.None);

        Assert.Equal(("Closed", "Automatic"), (cancelled!.Status, cancelled.ConfirmationSource));
        var row = await h.Db.Context.DossierActivities.AsNoTracking().SingleAsync(a => a.Id == plateau);
        Assert.Equal((DossierActivityExecutionStatus.Cancelled, "Klant had geen plateau nodig"), (row.ExecutionStatus, row.CancellationReason));
    }

    [Fact]
    public async Task Completion_RequiresAPlannedDate_NotInTheFuture_AndOnlyForStandaloneActivities()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Completed);
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan", new DateOnly(2026, 9, 30));
        var transportActivity = (await h.Current(dossier.Id)).Activities!.Single(a => a.HasStops).Id;

        var future = await Assert.ThrowsAsync<DomainValidationException>(() => h.Complete(dossier.Id, crane));
        Assert.Contains("30/09/2026", future.Message);

        var entity = await h.Db.Context.DossierActivities.SingleAsync(a => a.Id == crane);
        entity.PlannedDate = null;
        await h.Db.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Complete(dossier.Id, crane));

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Complete(dossier.Id, transportActivity));
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossier.Id)).Status);
    }

    [Fact]
    public async Task StartAndReopen_FollowTheStatusModel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan");
        // A second, still planned part keeps the dossier open throughout.
        await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "Plateau");

        var started = await h.Execution().StartAsync(dossier.Id, crane, new DossierActivityTransitionRequest(), CancellationToken.None);
        Assert.Equal("InProgress", started!.Activities!.Single(a => a.Id == crane).ExecutionStatus);
        await h.Complete(dossier.Id, crane);
        // A second completion is idempotent (no second audit row).
        await h.Complete(dossier.Id, crane);
        Assert.Single(await h.Db.Context.AuditLogs.AsNoTracking().Where(a => a.Action == "ActivityCompleted").ToListAsync());

        var reopened = await h.Execution().ReopenAsync(dossier.Id, crane, new DossierActivityTransitionRequest(), CancellationToken.None);
        var dto = reopened!.Activities!.Single(a => a.Id == crane);
        Assert.Equal(("Planned", (DateTime?)null), (dto.ExecutionStatus, dto.CompletedAt));
    }

    [Fact]
    public async Task ExecutionDetails_AreStored_AndTheEndIsDerived()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan");

        var updated = await h.Activities().UpdateAsync(dossier.Id, crane, new SaveDossierActivityRequest(
            await h.TypeIdAsync("KRAANWERK"), Label: "Kraan", PlannedDate: Yesterday, DurationHours: 2.5m,
            StartTime: "07:30", SiteAddress: "Werf B, Kaai 12, Antwerpen", WorkDescription: "Kabelhaspels lossen",
            Version: (await h.Current(dossier.Id)).Version), CancellationToken.None);

        var dto = updated!.Activities!.Single(a => a.Id == crane);
        Assert.Equal((new TimeOnly(7, 30), "Werf B, Kaai 12, Antwerpen", "Kabelhaspels lossen"), (dto.StartTime!.Value, dto.SiteAddress, dto.WorkDescription));
        Assert.Equal(new DateTime(2026, 9, 22, 10, 0, 0), dto.PlannedEnd);
        Assert.True(dto.IsExecutable);

        // Storage is not operational: no site/start time.
        var storage = await h.AddStandaloneAsync(dossier.Id, "OPSLAG", "Opslag");
        var storageType = await h.TypeIdAsync("OPSLAG");
        var version = (await h.Current(dossier.Id)).Version;
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Activities().UpdateAsync(dossier.Id, storage, new SaveDossierActivityRequest(
            storageType, Label: "Opslag", StartTime: "08:00", Version: version), CancellationToken.None));
    }

    [Fact]
    public async Task Assignment_ReusesTheConflictEngine_AbsenceAndCraneCapabilityBlock_OverrideNeedsAReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        var crane = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Kraan");
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var craneTruck = Guid.NewGuid();
        var van = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "E-1", FirstName = "Jan", LastName = "Peeters", IsActive = true });
        h.Db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = h.TenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        h.Db.Context.Vehicles.AddRange(
            new Vehicle { Id = craneTruck, TenantId = h.TenantId, InternalNumber = "KR-1", LicensePlate = "1-KRA-001", IsActive = true, HasCrane = true },
            new Vehicle { Id = van, TenantId = h.TenantId, InternalNumber = "BE-2", LicensePlate = "1-BES-002", IsActive = true, HasCrane = false });
        await h.Db.Context.SaveChangesAsync();

        var assigned = await h.Execution().AssignAsync(dossier.Id, crane,
            new AssignStandaloneActivityRequest(driverId, craneTruck, Version: (await h.Current(dossier.Id)).Version), overrideAllowed: false, CancellationToken.None);
        var dto = assigned!.Dossier.Activities!.Single(a => a.Id == crane);
        Assert.Equal(("Jan Peeters", "1-KRA-001"), (dto.DriverName, dto.VehiclePlate));

        // A vehicle without a crane for crane work: blocking, like an order that requires a crane.
        var noCrane = await Assert.ThrowsAsync<ActivityAssignmentConflictException>(() => h.Execution().AssignAsync(dossier.Id, crane,
            new AssignStandaloneActivityRequest(driverId, van), overrideAllowed: false, CancellationToken.None));
        Assert.Contains(noCrane.Conflicts, c => c.Code == PlanningConflictCode.OrderRequiresCrane && c.Blocking);

        // An approved absence on the planned day blocks; overriding needs the right AND a reason.
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, Type = AbsenceType.Vacation,
            StartDate = Yesterday, EndDate = Yesterday, Status = AbsenceStatus.Approved,
        });
        await h.Db.Context.SaveChangesAsync();
        var absent = await Assert.ThrowsAsync<ActivityAssignmentConflictException>(() => h.Execution().AssignAsync(dossier.Id, crane,
            new AssignStandaloneActivityRequest(driverId, craneTruck), overrideAllowed: false, CancellationToken.None));
        Assert.Contains(absent.Conflicts, c => c.Code == PlanningConflictCode.DriverAbsent);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Execution().AssignAsync(dossier.Id, crane,
            new AssignStandaloneActivityRequest(driverId, craneTruck, Override: true), overrideAllowed: true, CancellationToken.None));
        var overridden = await h.Execution().AssignAsync(dossier.Id, crane,
            new AssignStandaloneActivityRequest(driverId, craneTruck, Override: true, OverrideReason: "Verlof ingetrokken"), overrideAllowed: true, CancellationToken.None);
        Assert.NotNull(overridden);
    }

    // ------------------------------------------------------------ T-H8: accounting

    [Fact]
    public async Task Accounting_AgreedIsReady_GratisIsAValidFreeLine_UnpricedCompletedIsInReview()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        var agreed = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "A kraan");
        var free = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "B plateau");
        var unpriced = await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "C kraan");
        var plannedOnly = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "D plateau");
        h.Price(agreed, 400m);
        h.Price(free, null, free: true);
        h.Price(plannedOnly, 150m);
        await h.Db.Context.SaveChangesAsync();
        foreach (var id in new[] { agreed, free, unpriced })
        {
            await h.Complete(dossier.Id, id);
        }

        var control = await h.Control().GetAsync(CancellationToken.None);

        var proposal = Assert.Single(control.Proposals);
        Assert.Equal(new HashSet<Guid> { agreed, free }, proposal.Activities!.Select(a => a.DossierActivityId).ToHashSet());
        Assert.Equal(400m, proposal.TotalAmount);
        Assert.True(proposal.Activities!.Single(a => a.DossierActivityId == free).IsFree);
        Assert.False(proposal.Activities!.Single(a => a.DossierActivityId == agreed).IsFree);

        var review = Assert.Single(control.ActivitiesNeedingReview!);
        Assert.Equal((unpriced, dossier.DossierNumber, "Kraanwerk ter plaatse", "C kraan"), (review.DossierActivityId, review.DossierNumber, review.ActivityTypeName, review.Label));
        Assert.Equal(["activity.price_missing"], review.Reasons);

        // The planned (not completed) plateau is not invoice-ready yet — exactly like an order before completion.
        var candidates = await h.Invoices().ListUninvoicedActivitiesAsync(h.CustomerId, CancellationToken.None);
        Assert.Equal(new HashSet<Guid> { agreed, free }, candidates.Select(c => c.Id).ToHashSet());
    }

    // ------------------------------------------------------------ M11: one price-status computation

    [Fact]
    public async Task PriceCounts_AreIdenticalInListAndDetail_AndExcludeCancelledChildren()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        // 1) order with a price but an incomplete coverage (goods line without unit) → partial;
        var partialOrder = await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Confirmed, agreedPrice: 250m);
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = partialOrder, TariffDate = Yesterday, CoverageStatus = "Partial",
        });
        // 2) standalone unpriced; 3) standalone free; 4) standalone priced;
        await h.AddStandaloneAsync(dossier.Id, "KRAANWERK", "Zonder prijs");
        var free = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "Gratis");
        var priced = await h.AddStandaloneAsync(dossier.Id, "OPSLAG", "Opslag");
        h.Price(free, null, free: true);
        h.Price(priced, 300m);
        // 5) a cancelled child order and 6) a cancelled standalone activity — no components any more.
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Cancelled);
        var cancelledActivity = await h.AddStandaloneAsync(dossier.Id, "PLATEAU", "Geschrapt");
        await h.Db.Context.SaveChangesAsync();
        await h.Execution().CancelAsync(dossier.Id, cancelledActivity, new CancelDossierActivityRequest("Niet nodig"), CancellationToken.None);

        var detail = (await h.Current(dossier.Id)).Financials;
        var row = Assert.Single((await h.Dossiers().SearchAsync(new DossierSearchQuery(), CancellationToken.None)).Items);

        Assert.Equal((4, 2, 1, 1, 1), (detail.BillableActivityCount, detail.SettledActivityCount, detail.UnpricedActivityCount, detail.FreeActivityCount, detail.PartiallyPricedActivityCount));
        Assert.Equal(
            (detail.BillableActivityCount, detail.SettledActivityCount, detail.UnpricedActivityCount, detail.FreeActivityCount, detail.PartiallyPricedActivityCount, detail.PriceStatus),
            (row.BillableActivityCount, row.SettledActivityCount, row.UnpricedActivityCount, row.FreeActivityCount, row.PartiallyPricedActivityCount, row.PriceStatus));
        Assert.Equal("PartiallyPriced", row.PriceStatus);
        Assert.Equal(550m, row.AgreedPriceTotal);
        Assert.Equal(detail.AgreedOrderTotal, row.AgreedPriceTotal);

        Assert.Single((await h.Dossiers().SearchAsync(new DossierSearchQuery { PriceStatus = "Partial" }, CancellationToken.None)).Items);
        Assert.Empty((await h.Dossiers().SearchAsync(new DossierSearchQuery { PriceStatus = "Priced" }, CancellationToken.None)).Items);
        Assert.Empty((await h.Dossiers().SearchAsync(new DossierSearchQuery { PriceStatus = "Unpriced" }, CancellationToken.None)).Items);

        // The cancelled children are flagged out of the price counting on their activity cards too.
        var activities = (await h.Current(dossier.Id)).Activities!;
        Assert.Equal(4, activities.Count(a => a.CountsForPricing));
        // The confirmation dialog reads the same numbers.
        var summary = (await h.Lifecycle().EvaluateAsync(dossier.Id, CancellationToken.None))!.Summary;
        Assert.Equal((4, 2), (summary.BillableUnits, summary.PricedUnits));
    }

    [Fact]
    public async Task ACancelledOrder_IsNotAnUnpricedComponent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.NewDossierAsync();
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Confirmed, agreedPrice: 100m);
        await h.AddOrderAsync(dossier.Id, TransportOrderStatus.Cancelled);

        var detail = (await h.Current(dossier.Id)).Financials;
        var row = Assert.Single((await h.Dossiers().SearchAsync(new DossierSearchQuery(), CancellationToken.None)).Items);

        Assert.Equal((1, 1, "Priced"), (detail.BillableActivityCount, detail.SettledActivityCount, detail.PriceStatus));
        Assert.Equal((1, 1, "Priced"), (row.BillableActivityCount, row.SettledActivityCount, row.PriceStatus));
        Assert.Single((await h.Dossiers().SearchAsync(new DossierSearchQuery { PriceStatus = "Priced" }, CancellationToken.None)).Items);
    }
}
