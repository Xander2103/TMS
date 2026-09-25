using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Controllers;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Master sprint 2026-09-21 D6 (contract 4.3) — transport documents issued with a unique own number:
/// per-kind/year format, idempotent on the request id (also when two identical requests race),
/// distinct numbers under concurrent claims, an external number that is never overwritten, kinds
/// that must make sense for the order, and a PDF that prints document, dossier and order number.
/// </summary>
public class IssuedTransportDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid UserId, Guid TransportOrderId, Guid SecondTransportOrderId, Guid LiftingOrderId, Guid BareOrderId)
    {
        /// <summary>A service on its OWN context over the same database — what a second request looks like.</summary>
        public (IssuedTransportDocumentService Sut, TransportationDbContext Context) OnOwnContext(TestClock? clock = null)
        {
            var context = Db.CreateContextForTenant(TenantId);
            return (Build(context, TenantId, clock), context);
        }

        public IssuedTransportDocumentService Sut(Guid? tenantId = null, TestClock? clock = null) => Build(Db.Context, tenantId ?? TenantId, clock);

        private IssuedTransportDocumentService Build(TransportationDbContext context, Guid tenantId, TestClock? clock)
        {
            var tenant = new DevTenantContext(tenantId);
            return new IssuedTransportDocumentService(
                context, tenant, new AuditService(context, tenant, new DevCurrentUserContext(UserId)),
                new TransportDocumentService(context, tenant), clock ?? new TestClock(Now), new DevCurrentUserContext(UserId));
        }
    }

    private static async Task<Harness> SeedAsync(params IInterceptor[] interceptors)
    {
        var db = new SqliteTestDbContext(null, interceptors);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.LegalEntities.Add(new LegalEntity { Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV", IsActive = true });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "thalie@acme.test", FirstName = "Thalie", LastName = "Peeters", IsActive = true });

        Guid Order(string number, CraneJobKind kind, params StopType[] stops)
        {
            var id = Guid.NewGuid();
            db.Context.TransportOrders.Add(new TransportOrder
            {
                Id = id, TenantId = tenantId, CustomerId = customerId, OrderNumber = number, OrderDate = new DateOnly(2026, 9, 21),
                Status = TransportOrderStatus.Confirmed, GoodsDescription = "Kabelhaspels", CraneJobKind = kind,
                WorkDescription = kind == CraneJobKind.OnSiteLifting ? "Airco op dak hijsen" : null,
                Stops = stops.Select((type, index) => new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Sequence = index + 1, StopType = type, City = "Gent",
                }).ToList(),
            });
            return id;
        }

        var transport = Order("ORD-0001", CraneJobKind.None, StopType.Loading, StopType.Unloading);
        var second = Order("ORD-0002", CraneJobKind.None, StopType.Loading, StopType.Unloading);
        var lifting = Order("ORD-0003", CraneJobKind.OnSiteLifting, StopType.Site);
        var bare = Order("ORD-0004", CraneJobKind.None);
        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0042", Title = "Nexans", CustomerId = customerId,
        });
        await db.Context.SaveChangesAsync();
        db.Context.DossierOrders.AddRange(new[] { transport, second, lifting }.Select(orderId => new DossierOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId,
        }));
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId, transport, second, lifting, bare);
    }

    private static IssueTransportDocumentRequest Request(string kind, Guid? requestId = null, string? external = null) =>
        new(kind, requestId ?? Guid.NewGuid(), external);

    [Fact]
    public async Task Numbers_AreUniqueAcrossOrders_PerKindAndYear()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var cmr1 = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None))!;
        var cmr2 = (await h.Sut().IssueAsync(h.SecondTransportOrderId, Request("Cmr"), CancellationToken.None))!;
        var cmr3 = (await h.Sut().IssueAsync(h.TransportOrderId, Request("cmr"), CancellationToken.None))!; // several per order
        var note = (await h.Sut().IssueAsync(h.TransportOrderId, Request("DeliveryNote"), CancellationToken.None))!;
        var work = (await h.Sut().IssueAsync(h.LiftingOrderId, Request("WorkOrder"), CancellationToken.None))!;
        var nextYear = (await h.Sut(clock: new TestClock(Now.AddYears(1))).IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None))!;

        Assert.Equal(
            new[] { "CMR-2026-00001", "CMR-2026-00002", "CMR-2026-00003", "LB-2026-00001", "WB-2026-00001", "CMR-2027-00001" },
            new[] { cmr1, cmr2, cmr3, note, work, nextYear }.Select(d => d.DocumentNumber));
        Assert.Equal((IssuedTransportDocumentKind.Cmr, "Thalie Peeters", Now.UtcDateTime), (cmr1.Kind, cmr1.IssuedByName, cmr1.IssuedAt));

        // The 2026 series is untouched by the new year, and the list is per order, oldest first.
        Assert.Equal(4, (await h.Db.Context.TransportDocumentSequences.SingleAsync(s => s.Kind == IssuedTransportDocumentKind.Cmr && s.Year == 2026)).NextValue);
        Assert.Equal(new[] { cmr2.Id }, (await h.Sut().ListAsync(h.SecondTransportOrderId, CancellationToken.None))!.Select(d => d.Id));
        var stored = await h.Db.Context.IssuedTransportDocuments.AsNoTracking().SingleAsync(d => d.Id == cmr1.Id);
        Assert.NotNull(stored.DossierId); // the owning dossier at issue time
    }

    [Fact]
    public async Task SameRequestId_ReturnsTheSameRecord_AndClaimsNoSecondNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var requestId = Guid.NewGuid();

        var first = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr", requestId, "EXT-1"), CancellationToken.None))!;
        var again = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr", requestId, "EXT-ANDERS"), CancellationToken.None))!;

        Assert.Equal((first.Id, first.DocumentNumber, "EXT-1"), (again.Id, again.DocumentNumber, again.ExternalNumber));
        Assert.Single(h.Db.Context.IssuedTransportDocuments);
        Assert.Equal(2, (await h.Db.Context.TransportDocumentSequences.SingleAsync()).NextValue);

        // The key belongs to ITS order: never another order's document under this route.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut().IssueAsync(h.SecondTransportOrderId, Request("Cmr", requestId), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut().IssueAsync(h.TransportOrderId, new IssueTransportDocumentRequest("Cmr", Guid.Empty), CancellationToken.None));
    }

    /// <summary>
    /// Two identical requests at the same moment: both pass the "already issued?" lookup, the second
    /// insert hits the unique (TenantId, RequestId) index — it must reload and answer with the
    /// winner's record instead of failing or claiming a second number.
    /// </summary>
    [Fact]
    public async Task IdenticalRequestsRacing_TheLoserReturnsTheWinnersRecord()
    {
        var competitor = new CompetingIssueInterceptor();
        var h = await SeedAsync(competitor);
        using var _ = h.Db;
        var requestId = Guid.NewGuid();
        competitor.Configure(h, requestId);

        var answered = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr", requestId), CancellationToken.None))!;

        Assert.Equal(1, competitor.Inserts);
        var stored = await h.Db.Context.IssuedTransportDocuments.AsNoTracking().SingleAsync();
        Assert.Equal((stored.Id, "CMR-2026-00001"), (answered.Id, answered.DocumentNumber));
        Assert.Equal(2, (await h.Db.Context.TransportDocumentSequences.AsNoTracking().SingleAsync()).NextValue); // one number claimed, not two

        // The context is clean afterwards: the next issue simply continues the series.
        var next = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None))!;
        Assert.Equal("CMR-2026-00002", next.DocumentNumber);
    }

    /// <summary>
    /// Parallel issue requests on SEPARATE contexts over one database. Every context first issues a
    /// document, so it tracks the sequence row; the parallel round then starts from stale counters —
    /// exactly the production race. The concurrency token + retry must hand out distinct numbers.
    /// </summary>
    [Fact]
    public async Task ParallelIssueRequests_OnSeparateContexts_ProduceDistinctNumbers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var workers = Enumerable.Range(0, 6).Select(_ => h.OnOwnContext()).ToList();
        try
        {
            foreach (var (sut, _) in workers)
            {
                await sut.IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None);
            }

            // One request per context (a DbContext serves one request at a time), alternating orders.
            var parallel = await Task.WhenAll(workers.Select((w, index) => w.Sut.IssueAsync(
                index % 2 == 0 ? h.TransportOrderId : h.SecondTransportOrderId, Request("Cmr"), CancellationToken.None)));

            var numbers = await h.Db.Context.IssuedTransportDocuments.AsNoTracking().Select(d => d.DocumentNumber).ToListAsync();
            Assert.Equal(12, numbers.Count);
            Assert.Equal(12, numbers.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 12).Select(i => $"CMR-2026-{i:D5}"), numbers.OrderBy(n => n));
            Assert.Equal(6, parallel.Select(d => d!.DocumentNumber).Distinct().Count());
        }
        finally
        {
            workers.ForEach(w => w.Context.Dispose());
        }
    }

    [Fact]
    public async Task ASequenceThatFellBehind_SkipsToTheFirstFreeNumber_AndNeverReusesOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None);
        await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None);
        await h.Db.Context.Database.ExecuteSqlRawAsync("UPDATE transport_document_sequences SET \"NextValue\" = 1");
        foreach (var entry in h.Db.Context.ChangeTracker.Entries<TransportDocumentSequence>().ToList())
        {
            entry.State = EntityState.Detached;
        }

        var issued = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None))!;

        Assert.Equal("CMR-2026-00003", issued.DocumentNumber); // the unique index is the final arbiter
        Assert.Equal(3, await h.Db.Context.IssuedTransportDocuments.CountAsync());
    }

    [Fact]
    public async Task ExternalNumber_IsStoredAsEntered_NextToOurOwnNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var issued = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr", external: "  BE-CMR-778812  "), CancellationToken.None))!;

        Assert.Equal(("CMR-2026-00001", "BE-CMR-778812"), (issued.DocumentNumber, issued.ExternalNumber));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr", external: new string('9', 61)), CancellationToken.None));
    }

    [Fact]
    public async Task Kind_MustMakeSenseForTheOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // On-site lifting: a work order — never a CMR or delivery note.
        var cmrOnLifting = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut().IssueAsync(h.LiftingOrderId, Request("Cmr"), CancellationToken.None));
        Assert.Contains("werkbon", cmrOnLifting.Message);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut().IssueAsync(h.LiftingOrderId, Request("DeliveryNote"), CancellationToken.None));
        Assert.Equal("WB-2026-00001", (await h.Sut().IssueAsync(h.LiftingOrderId, Request("WorkOrder"), CancellationToken.None))!.DocumentNumber);

        // A goods transport gets no work order; an order without loading/unloading stop gets no CMR.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut().IssueAsync(h.TransportOrderId, Request("WorkOrder"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut().IssueAsync(h.BareOrderId, Request("Cmr"), CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut().IssueAsync(h.TransportOrderId, Request("Invoice"), CancellationToken.None));
        Assert.Contains("Onbekende documentsoort", unknown.Message);

        Assert.Single(h.Db.Context.IssuedTransportDocuments); // refusals claimed no number
    }

    [Theory]
    [InlineData("Cmr", "CMR-2026-00001", "EXT-99")]
    [InlineData("DeliveryNote", "LB-2026-00001", null)]
    public async Task Pdf_PrintsDocumentDossierAndOrderNumber(string kind, string expectedNumber, string? external)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = (await h.Sut().IssueAsync(h.TransportOrderId, Request(kind, external: external), CancellationToken.None))!;

        var pdf = (await h.Sut().RenderPdfAsync(issued.Id, CancellationToken.None))!.Value;

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        Assert.Equal($"{expectedNumber}.pdf", pdf.FileName);
        Assert.Equal(new TransportDocumentReference(expectedNumber, "DOS-0042", external), pdf.Snapshot.Reference);
        Assert.Equal("ORD-0001", pdf.Snapshot.OrderNumber);

        var text = PageText(pdf.Content);
        Assert.Contains(expectedNumber, text);
        Assert.Contains("DOS-0042", text);
        Assert.Contains("ORD-0001", text);
        Assert.Equal(external is not null, text.Contains("Extern nr."));
        if (external is not null)
        {
            Assert.Contains(external, text);
        }
    }

    [Fact]
    public async Task WorkOrderPdf_CarriesTheReferenceBlock_AndTheStreamedDocumentStaysNumberless()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = (await h.Sut().IssueAsync(h.LiftingOrderId, Request("WorkOrder"), CancellationToken.None))!;

        var text = PageText((await h.Sut().RenderPdfAsync(issued.Id, CancellationToken.None))!.Value.Content);
        Assert.Contains("WERKBON", text);
        Assert.Contains("WB-2026-00001", text);
        Assert.Contains("DOS-0042", text);
        Assert.Contains("ORD-0003", text);

        // The existing streamed endpoint is unchanged: no document number, no dossier number.
        var streamed = (await new TransportDocumentService(h.Db.Context, new DevTenantContext(h.TenantId))
            .RenderAsync(h.LiftingOrderId, "work-order", CancellationToken.None))!.Value;
        var streamedText = PageText(streamed.Content);
        Assert.DoesNotContain("Documentnr.", streamedText);
        Assert.DoesNotContain("DOS-0042", streamedText);
    }

    [Fact]
    public async Task AnotherTenant_CanNeitherListIssueNorRender()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = (await h.Sut().IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None))!;
        var foreign = h.Sut(tenantId: Guid.NewGuid());

        Assert.Null(await foreign.ListAsync(h.TransportOrderId, CancellationToken.None));
        Assert.Null(await foreign.IssueAsync(h.TransportOrderId, Request("Cmr"), CancellationToken.None));
        Assert.Null(await foreign.RenderPdfAsync(issued.Id, CancellationToken.None));
        Assert.Single(h.Db.Context.IssuedTransportDocuments);
    }

    [Fact]
    public void Endpoints_ReadWithOrdersView_IssueWithOrdersEdit()
    {
        static IReadOnlyList<string> Codes(string action) =>
            typeof(IssuedTransportDocumentsController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
                .GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;

        Assert.Equal(new[] { PermissionCodes.OrdersView, PermissionCodes.OrdersManage }, Codes(nameof(IssuedTransportDocumentsController.List)));
        Assert.Equal(new[] { PermissionCodes.OrdersView, PermissionCodes.OrdersManage }, Codes(nameof(IssuedTransportDocumentsController.Pdf)));
        Assert.Equal(new[] { PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage }, Codes(nameof(IssuedTransportDocumentsController.Issue)));
    }

    /// <summary>Content streams are Flate-compressed; the decoded stream carries the literal text operators.</summary>
    private static string PageText(byte[] pdf)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        Assert.Equal(1, document.PageCount);
        return System.Text.Encoding.Latin1.GetString(document.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue);
    }

    /// <summary>Simulates the identical request winning the race: once, right before the first issued
    /// document commits, it writes a document with the SAME request id through a second context.</summary>
    private sealed class CompetingIssueInterceptor : SaveChangesInterceptor
    {
        private Harness? _harness;
        private Guid _requestId;
        public int Inserts { get; private set; }

        public void Configure(Harness harness, Guid requestId)
        {
            _harness = harness;
            _requestId = requestId;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_harness is not null && Inserts == 0 && eventData.Context is { } context
                && context.ChangeTracker.Entries<IssuedTransportDocument>().Any(e => e.State == EntityState.Added && e.Entity.RequestId == _requestId))
            {
                Inserts++;
                var (winner, winnerContext) = _harness.OnOwnContext();
                await using (winnerContext)
                {
                    await winner.IssueAsync(_harness.TransportOrderId, new IssueTransportDocumentRequest("Cmr", _requestId), cancellationToken);
                }
            }

            return result;
        }
    }
}
