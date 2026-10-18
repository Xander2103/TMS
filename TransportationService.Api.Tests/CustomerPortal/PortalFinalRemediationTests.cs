using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Entities;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>
/// Customer portal final remediation (2026-10-18): the info-request round trip, one customer-facing
/// status over drafts/redeliveries, the visibility rule on every portal surface, upcoming
/// deliveries, offset timestamps (KP-12), read markers (KP-10) and issued CMR/LB publication (KP-06)
/// — for Customer A and, where it matters, against Customer B.
/// </summary>
public class PortalFinalRemediationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UnloadFrom = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UnloadTo = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerA, Guid CustomerB, Guid PortalUserA, Guid PortalUserB, Guid PlannerId, string StorageRoot)
        : IDisposable
    {
        public DevTenantContext Tenant => new(TenantId);

        public TransportOrderService Orders(Guid? userId)
        {
            var user = new DevCurrentUserContext(userId);
            return new TransportOrderService(Db.Context, Tenant, new AuditService(Db.Context, Tenant, user), new TestClock(Now), currentUser: user);
        }

        public CustomerPortalService Portal(Guid userId)
        {
            var user = new DevCurrentUserContext(userId);
            var audit = new AuditService(Db.Context, Tenant, user);
            return new CustomerPortalService(Db.Context, Tenant, user, Orders(userId),
                new LocationService(Db.Context, Tenant, audit, new CountryCodeValidator(Db.Context)), audit);
        }

        public CustomerMessageService Messages(Guid userId)
        {
            var user = new DevCurrentUserContext(userId);
            return new CustomerMessageService(Db.Context, Tenant, user, new AuditService(Db.Context, Tenant, user), new TestClock(Now));
        }

        public OrderPortalReviewService Review()
        {
            var user = new DevCurrentUserContext(PlannerId);
            return new OrderPortalReviewService(Db.Context, Tenant, Orders(PlannerId), Messages(PlannerId),
                new AuditService(Db.Context, Tenant, user), null, NullLogger<OrderPortalReviewService>.Instance, user);
        }

        public IssuedTransportDocumentService Issued()
        {
            var user = new DevCurrentUserContext(PlannerId);
            return new IssuedTransportDocumentService(Db.Context, Tenant, new AuditService(Db.Context, Tenant, user),
                new TransportDocumentService(Db.Context, Tenant), new TestClock(Now), user);
        }

        public PortalDocumentService Documents(Guid userId) =>
            new(Db.Context, Tenant, new DevCurrentUserContext(userId), new LocalFileStorageService(StorageRoot), Issued());

        public void Dispose()
        {
            Db.Dispose();
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var portalA = Guid.NewGuid();
        var portalB = Guid.NewGuid();
        var planner = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1 });
        db.Context.LegalEntities.Add(new LegalEntity { Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true });
        db.Context.Customers.AddRange(
            new Customer { Id = customerA, TenantId = tenantId, CustomerNumber = "KL-A", Name = "TEST PORTAL Customer A", IsActive = true },
            new Customer { Id = customerB, TenantId = tenantId, CustomerNumber = "KL-B", Name = "TEST PORTAL Customer B", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = portalA, TenantId = tenantId, Email = "a@portal.test", FirstName = "Anna", LastName = "A", CustomerId = customerA, IsActive = true },
            new User { Id = portalB, TenantId = tenantId, Email = "b@portal.test", FirstName = "Bart", LastName = "B", CustomerId = customerB, IsActive = true },
            new User { Id = planner, TenantId = tenantId, Email = "planner@acme.test", FirstName = "Pia", LastName = "Planner", IsActive = true });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);
        var storage = Path.Combine(Path.GetTempPath(), "portal-final-" + Guid.NewGuid().ToString("N"));
        return new Harness(db, tenantId, customerA, customerB, portalA, portalB, planner, storage);
    }

    private static PortalCreateOrderRequest Request(DateTime? unloadFrom = null, DateTime? unloadTo = null) => new(
        CustomerReference: "PO-A", OrderDate: new DateOnly(2026, 10, 20), GoodsDescription: "Paletten", Remarks: null,
        Stops:
        [
            new PortalStopInput(StopType.Loading, null, "Laadkaai", "Kaai 1", "2000", "Antwerpen", "BE", null, null, null, null),
            new PortalStopInput(StopType.Unloading, null, "Loskaai", "Dorpsstraat 1", "9000", "Gent", "BE",
                unloadFrom ?? UnloadFrom, unloadTo ?? UnloadTo, null, null),
        ],
        CargoItems: [new PortalCargoInput("Europalletten", 4, null, PackageUnitType.EuroPallet)]);

    private static async Task<Guid> SubmitAsync(Harness h, Guid? user = null, PortalCreateOrderRequest? request = null)
    {
        var result = await h.Portal(user ?? h.PortalUserA).SubmitOrderAsync(request ?? Request(), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        return result.Value!.Id;
    }

    private static async Task<Guid> AddOrderAsync(Harness h, Guid customerId, string number, TransportOrderStatus status,
        Guid? redeliveryOf = null, DateTime? unloadFrom = null)
    {
        var id = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = id, TenantId = h.TenantId, CustomerId = customerId, OrderNumber = number, OrderDate = new DateOnly(2026, 10, 18),
            Status = status, RedeliveryOfOrderId = redeliveryOf,
            Stops =
            [
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 2, StopType = StopType.Unloading, City = "Gent",
                    PlannedFrom = unloadFrom, PlannedTo = unloadFrom?.AddHours(2),
                },
            ],
        });
        await h.Db.Context.SaveChangesAsync();
        return id;
    }

    private static async Task ExecuteDropAsync(Harness h, Guid orderId, StopExecutionStatus status)
    {
        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip { Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-" + orderId.ToString("N")[..6], TripDate = new DateOnly(2026, 10, 18) });
        var drop = await h.Db.Context.TransportOrderStops.SingleAsync(s => s.TransportOrderId == orderId && s.StopType == StopType.Unloading);
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = drop.Id, Status = status, CompletedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    // ---- Info request round trip ----------------------------------------------------------------

    [Fact]
    public async Task InfoRequest_CustomerSeesTheQuestion_AnswersOnTheThread_PlannerSeesTheAnswer_ThenAccepts()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h);
        Assert.Equal(PortalCustomerStatuses.Received, (await h.Portal(h.PortalUserA).GetMyOrderAsync(orderId, CancellationToken.None)).Value!.Status);

        var asked = await h.Review().ReviewAsync(orderId,
            new PortalReviewRequest(PortalReviewAction.RequestInfo, "Wat is het exacte gewicht per pallet?"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, asked.Outcome);

        // The customer clearly sees that information is requested, and which.
        var waiting = (await h.Portal(h.PortalUserA).GetMyOrderAsync(orderId, CancellationToken.None)).Value!;
        Assert.Equal(PortalCustomerStatuses.InfoRequested, waiting.Status);
        Assert.Equal("Wat is het exacte gewicht per pallet?", waiting.InfoRequest!.Question);
        Assert.Null(waiting.InfoRequest.AnsweredAt);
        var list = (await h.Portal(h.PortalUserA).ListMyOrdersAsync(CancellationToken.None)).Value!;
        Assert.Equal(PortalCustomerStatuses.InfoRequested, list.Single(o => o.Id == orderId).Status);

        // The customer answers through the supported portal flow (the order's thread).
        var answered = await h.Messages(h.PortalUserA).SendPortalAsync(
            new SendCustomerMessageRequest(orderId, "650 kg per pallet."), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, answered.Outcome);

        var reviewing = (await h.Portal(h.PortalUserA).GetMyOrderAsync(orderId, CancellationToken.None)).Value!;
        Assert.Equal(PortalCustomerStatuses.InReview, reviewing.Status);
        Assert.NotNull(reviewing.InfoRequest!.AnsweredAt);

        // The planner sees the answer where they review — no phone, no DB edit.
        var state = await h.Review().GetStateAsync(orderId, CancellationToken.None);
        Assert.Equal("Wat is het exacte gewicht per pallet?", state!.InfoRequest!.Question);
        Assert.Equal("650 kg per pallet.", Assert.Single(state.InfoRequest.Answers).Body);

        var accepted = await h.Review().ReviewAsync(orderId, new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, accepted.Outcome);
        var confirmed = (await h.Portal(h.PortalUserA).GetMyOrderAsync(orderId, CancellationToken.None)).Value!;
        Assert.Equal(PortalCustomerStatuses.Confirmed, confirmed.Status);
        Assert.Null(confirmed.InfoRequest); // the question is history once decided
    }

    [Fact]
    public async Task InfoRequest_MessageIsMarked_AndOnlyTheOwningCustomerCanAnswerIt()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h);
        await h.Review().ReviewAsync(orderId, new PortalReviewRequest(PortalReviewAction.RequestInfo, "Laadadres bevestigen?"), CancellationToken.None);

        var thread = (await h.Messages(h.PortalUserA).ListPortalAsync(orderId, CancellationToken.None)).Value!;
        Assert.True(Assert.Single(thread).IsInfoRequest);

        // Customer B can neither read nor answer A's request: indistinguishable from non-existent.
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Messages(h.PortalUserB).ListPortalAsync(orderId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Messages(h.PortalUserB).SendPortalAsync(
            new SendCustomerMessageRequest(orderId, "Ik ben B"), CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Portal(h.PortalUserB).GetMyOrderAsync(orderId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalCustomerStatuses.InfoRequested, (await h.Portal(h.PortalUserA).GetMyOrderAsync(orderId, CancellationToken.None)).Value!.Status);
    }

    // ---- Visibility rule on every portal surface ------------------------------------------------

    [Fact]
    public async Task InternalDraft_ThreadAndPublishedDocuments_AreNotReachableFromThePortal()
    {
        using var h = await SeedAsync();
        var draftId = await AddOrderAsync(h, h.CustomerA, "ORD-DRAFT", TransportOrderStatus.Draft);
        // Staff wrote on the draft's thread and published a document on it — still internal work.
        await h.Messages(h.PlannerId).SendToCustomerAsync(h.CustomerA, new SendCustomerMessageRequest(draftId, "Intern concept"), CancellationToken.None);
        Directory.CreateDirectory(h.StorageRoot);
        var storage = new LocalFileStorageService(h.StorageRoot);
        await using (var content = new MemoryStream("concept"u8.ToArray()))
        {
            var path = await storage.SaveAsync(h.TenantId, "orders", "concept.pdf", content, CancellationToken.None);
            h.Db.Context.TransportOrderDocuments.Add(new TransportOrderDocument
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = draftId, Title = "Concept", FileName = "concept.pdf",
                ContentType = "application/pdf", DocumentPath = path, CustomerVisible = true,
            });
            await h.Db.Context.SaveChangesAsync();
        }

        var documentId = h.Db.Context.TransportOrderDocuments.Single().Id;
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Portal(h.PortalUserA).GetMyOrderAsync(draftId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Messages(h.PortalUserA).ListPortalAsync(draftId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Messages(h.PortalUserA).MarkPortalReadAsync(draftId, CancellationToken.None)).Outcome);
        Assert.Equal(0, (await h.Messages(h.PortalUserA).GetPortalUnreadCountAsync(CancellationToken.None)).Value!.Count);
        Assert.DoesNotContain((await h.Documents(h.PortalUserA).ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == documentId);
        Assert.Equal(PortalOutcomeKind.NotFound,
            (await h.Documents(h.PortalUserA).GetDocumentContentAsync(PortalDocumentSource.OrderDocument, documentId, CancellationToken.None)).Outcome);
        Assert.DoesNotContain((await h.Portal(h.PortalUserA).ListMyOrdersAsync(CancellationToken.None)).Value!, o => o.Id == draftId);
    }

    [Fact]
    public async Task MarkRead_IsIdempotent()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h);
        await h.Messages(h.PlannerId).SendToCustomerAsync(h.CustomerA, new SendCustomerMessageRequest(orderId, "Hallo"), CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.Success, (await h.Messages(h.PortalUserA).MarkPortalReadAsync(orderId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.Success, (await h.Messages(h.PortalUserA).MarkPortalReadAsync(orderId, CancellationToken.None)).Outcome);
        Assert.Equal(1, h.Db.Context.CustomerMessageReads.Count(r => r.UserId == h.PortalUserA && r.TransportOrderId == orderId));
    }

    // ---- Failed delivery / redelivery ----------------------------------------------------------

    [Fact]
    public async Task FailedDelivery_ThenRedelivery_TellsTheCustomerTheTruth_AndADraftRedeliveryIsNotAPromise()
    {
        using var h = await SeedAsync();
        var original = await AddOrderAsync(h, h.CustomerA, "ORD-F", TransportOrderStatus.Completed);
        await ExecuteDropAsync(h, original, StopExecutionStatus.Failed);
        var portal = h.Portal(h.PortalUserA);
        Assert.Equal(PortalCustomerStatuses.NotDelivered, (await portal.GetMyOrderAsync(original, CancellationToken.None)).Value!.Status);

        // An internal draft redelivery is planning work in progress, not "Herlevering gepland".
        var redelivery = await AddOrderAsync(h, h.CustomerA, "ORD-R", TransportOrderStatus.Draft, redeliveryOf: original);
        var stillFailed = (await portal.GetMyOrderAsync(original, CancellationToken.None)).Value!;
        Assert.Equal(PortalCustomerStatuses.NotDelivered, stillFailed.Status);
        Assert.Null(stillFailed.RedeliveryOrderId);

        var redeliveryOrder = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == redelivery);
        redeliveryOrder.Status = TransportOrderStatus.Confirmed;
        await h.Db.Context.SaveChangesAsync();
        var planned = (await portal.GetMyOrderAsync(original, CancellationToken.None)).Value!;
        Assert.Equal(PortalCustomerStatuses.RedeliveryPlanned, planned.Status);
        Assert.Equal(redelivery, planned.RedeliveryOrderId);

        redeliveryOrder.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();
        await ExecuteDropAsync(h, redelivery, StopExecutionStatus.Completed);
        Assert.Equal(PortalCustomerStatuses.Redelivered, (await portal.GetMyOrderAsync(original, CancellationToken.None)).Value!.Status);
        Assert.Equal(PortalCustomerStatuses.Delivered, (await portal.GetMyOrderAsync(redelivery, CancellationToken.None)).Value!.Status);

        // Never the internal lifecycle: the timeline's close of the failed original says so.
        var timeline = (await portal.GetMyOrderAsync(original, CancellationToken.None)).Value!.Timeline;
        Assert.DoesNotContain(timeline, e => e.Status is "Completed" or "Invoiced" or "Draft");
    }

    // ---- Upcoming deliveries ------------------------------------------------------------------

    [Fact]
    public async Task UpcomingDeliveries_OnlyAcceptedWorkStillToBeDelivered_OneRowPerOrder()
    {
        using var h = await SeedAsync();
        var soon = Now.UtcDateTime.AddDays(2);
        await SubmitAsync(h, request: Request(soon, soon.AddHours(2))); // an unanswered request is not a promise
        var confirmed = await AddOrderAsync(h, h.CustomerA, "ORD-C", TransportOrderStatus.Confirmed, unloadFrom: soon);
        var failedStop = await AddOrderAsync(h, h.CustomerA, "ORD-X", TransportOrderStatus.InProgress, unloadFrom: soon);
        await ExecuteDropAsync(h, failedStop, StopExecutionStatus.Failed);
        await AddOrderAsync(h, h.CustomerA, "ORD-D", TransportOrderStatus.Completed, unloadFrom: soon);
        await AddOrderAsync(h, h.CustomerA, "ORD-INTERNAL", TransportOrderStatus.Draft, unloadFrom: soon);
        await AddOrderAsync(h, h.CustomerB, "ORD-B", TransportOrderStatus.Confirmed, unloadFrom: soon);
        var secondDrop = new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = confirmed, Sequence = 3, StopType = StopType.Unloading,
            City = "Brugge", PlannedFrom = soon.AddHours(5),
        };
        h.Db.Context.TransportOrderStops.Add(secondDrop);
        await h.Db.Context.SaveChangesAsync();

        var user = new DevCurrentUserContext(h.PortalUserA);
        var audit = new AuditService(h.Db.Context, h.Tenant, user);
        var clock = new TestClock(Now);
        var invoices = new Modules.Invoicing.Services.InvoiceService(h.Db.Context, h.Tenant, audit, clock,
            new Modules.Invoicing.Services.InvoiceNumberService(h.Db.Context, h.Tenant),
            new Modules.Partners.Services.CustomerBillingConfigService(h.Db.Context, h.Tenant, audit, clock),
            new Modules.Accounting.Services.AccountingService(h.Db.Context, h.Tenant, audit));
        var dashboard = await new PortalDashboardService(h.Db.Context, h.Tenant, user, h.Messages(h.PortalUserA), invoices,
                new PortalAnnouncementService(h.Db.Context, h.Tenant, user, audit, clock), clock)
            .GetDashboardAsync(CancellationToken.None);

        var upcoming = dashboard.Value!.UpcomingDeliveries;
        var row = Assert.Single(upcoming);
        Assert.Equal("ORD-C", row.OrderNumber);
        Assert.Equal("Gent", row.City); // its NEXT drop
        // The request still counts as active (it is being reviewed), the internal draft does not.
        Assert.Equal(3, dashboard.Value.ActiveOrders);
    }

    // ---- KP-12 ----------------------------------------------------------------------------------

    [Fact]
    public async Task OffsetTimes_AreStoredAsUtc_NotA500()
    {
        using var h = await SeedAsync();
        var local = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.FromHours(2)).LocalDateTime; // Kind=Local, as JSON binds "+02:00"
        var orderId = await SubmitAsync(h, request: Request(local, local.AddHours(2)));

        var drop = h.Db.Context.TransportOrderStops.Single(s => s.TransportOrderId == orderId && s.StopType == StopType.Unloading);
        Assert.Equal(new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc), drop.RequestedFrom);
        Assert.Equal(new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc), drop.PlannedFrom); // the planner's window (P1)
    }

    // ---- KP-06 ----------------------------------------------------------------------------------

    [Fact]
    public async Task IssuedCmr_PublishedToTheOwningCustomerOnly_DownloadsTheFrozenSnapshot_AndFollowsReplacement()
    {
        using var h = await SeedAsync();
        var orderId = await AddOrderAsync(h, h.CustomerA, "ORD-CMR", TransportOrderStatus.Confirmed);
        var cmr = (await h.Issued().IssueAsync(orderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid()), CancellationToken.None))!;

        // Internal until published.
        Assert.DoesNotContain((await h.Documents(h.PortalUserA).ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == cmr.Id);
        Assert.Equal(PortalOutcomeKind.NotFound,
            (await h.Documents(h.PortalUserA).GetDocumentContentAsync(PortalDocumentSource.IssuedTransportDocument, cmr.Id, CancellationToken.None)).Outcome);

        Assert.True((await h.Issued().SetCustomerVisibleAsync(cmr.Id, true, CancellationToken.None))!.CustomerVisible);
        var listed = (await h.Documents(h.PortalUserA).ListMyDocumentsAsync(CancellationToken.None)).Value!;
        Assert.Contains(listed, d => d.Id == cmr.Id && d.Source == PortalDocumentSource.IssuedTransportDocument && d.Title == cmr.DocumentNumber);

        // Same frozen bytes as the office's own download.
        var portalPdf = (await h.Documents(h.PortalUserA).GetDocumentContentAsync(PortalDocumentSource.IssuedTransportDocument, cmr.Id, CancellationToken.None)).Value!;
        var officePdf = (await h.Issued().RenderPdfAsync(cmr.Id, CancellationToken.None))!.Value;
        Assert.Equal(officePdf.Content, portalPdf.Content);

        // Customer B: not listed, 404 on the id.
        Assert.DoesNotContain((await h.Documents(h.PortalUserB).ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == cmr.Id);
        Assert.Equal(PortalOutcomeKind.NotFound,
            (await h.Documents(h.PortalUserB).GetDocumentContentAsync(PortalDocumentSource.IssuedTransportDocument, cmr.Id, CancellationToken.None)).Outcome);

        // A corrected CMR replaces the published one for the customer too; the old one disappears.
        var corrected = (await h.Issued().IssueAsync(orderId,
            new IssueTransportDocumentRequest("Cmr", Guid.NewGuid(), null, cmr.Id, "Gewicht gecorrigeerd"), CancellationToken.None))!;
        Assert.True(corrected.CustomerVisible);
        listed = (await h.Documents(h.PortalUserA).ListMyDocumentsAsync(CancellationToken.None)).Value!;
        Assert.Contains(listed, d => d.Id == corrected.Id);
        Assert.DoesNotContain(listed, d => d.Id == cmr.Id);
        Assert.Equal(PortalOutcomeKind.NotFound,
            (await h.Documents(h.PortalUserA).GetDocumentContentAsync(PortalDocumentSource.IssuedTransportDocument, cmr.Id, CancellationToken.None)).Outcome);

        // A replaced document can never be (re)published.
        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() => h.Issued().SetCustomerVisibleAsync(cmr.Id, true, CancellationToken.None));

        // Withdrawing removes it again.
        await h.Issued().SetCustomerVisibleAsync(corrected.Id, false, CancellationToken.None);
        Assert.DoesNotContain((await h.Documents(h.PortalUserA).ListMyDocumentsAsync(CancellationToken.None)).Value!,
            d => d.Source == PortalDocumentSource.IssuedTransportDocument);
    }

    [Fact]
    public async Task IssuedCmr_PublicationIsWithdrawn_WhenTheOrderMovesToAnotherCustomer()
    {
        using var h = await SeedAsync();
        var orderId = await AddOrderAsync(h, h.CustomerA, "ORD-MOVE", TransportOrderStatus.Confirmed);
        var cmr = (await h.Issued().IssueAsync(orderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid()), CancellationToken.None))!;
        await h.Issued().SetCustomerVisibleAsync(cmr.Id, true, CancellationToken.None);

        var withdrawn = await DocumentPublicationWithdrawal.WithdrawIssuedForOrderAsync(
            h.Db.Context, h.TenantId, orderId, new AuditService(h.Db.Context, h.Tenant, new DevCurrentUserContext(h.PlannerId)),
            h.CustomerA, h.CustomerB, CancellationToken.None);
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
        order.CustomerId = h.CustomerB;
        await h.Db.Context.SaveChangesAsync();

        Assert.Equal(1, withdrawn);
        Assert.DoesNotContain((await h.Documents(h.PortalUserB).ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == cmr.Id);
        Assert.DoesNotContain((await h.Documents(h.PortalUserA).ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == cmr.Id);
    }
}
