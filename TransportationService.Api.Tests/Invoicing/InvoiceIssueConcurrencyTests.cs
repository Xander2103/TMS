using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// PERF-B2 (performance/concurrency final remediation 2026-10-21): two "Uitreiken" clicks on the same
/// draft both passed the transition check and both claimed a number — the loser took N+1 and
/// overwrote the invoice (N burned, "Issued" audited twice). Now <c>Invoice.Status</c> is a
/// concurrency token (the UPDATE is the conditional Draft → Sent transition), the loser answers 409
/// before or at the claim and leaves nothing behind; and a burst of DIFFERENT drafts issued in
/// parallel takes contiguous numbers instead of the misfired "andere nummerreeks" 400.
/// </summary>
public class InvoiceIssueConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, CompetingWriterInterceptor Race, Guid TenantId, Guid CustomerId, LegalEntity Entity, Guid CategoryId)
    {
        public InvoiceService Sut(TransportationDbContext? context = null)
        {
            var db = context ?? Db.Context;
            var tenant = new DevTenantContext(TenantId);
            var audit = new AuditService(db, tenant, new DevCurrentUserContext(null));
            return new InvoiceService(db, tenant, audit, new TestClock(Now), new InvoiceNumberService(db, tenant),
                new CustomerBillingConfigService(db, tenant, audit, new TestClock(Now)),
                new AccountingService(db, tenant, audit));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var race = new CompetingWriterInterceptor();
        var db = new SqliteTestDbContext(null, race);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, PaymentTermDays = 30 });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Transport BV",
            InvoiceNumberFormat = "{YYYY}{MM}{SEQ}", InvoiceSequencePadding = 4, IsActive = true, IsDefault = true,
        };
        db.Context.LegalEntities.Add(entity);
        await db.Context.SaveChangesAsync();
        var categoryId = await AccountingTestSeed.TransportCategoryAsync(db.Context, tenantId);
        return new Harness(db, race, tenantId, customerId, entity, categoryId);
    }

    private static CreateInvoiceRequest Request(Harness h) => new(
        h.CustomerId, null, [], [new ManualInvoiceLineInput("Transport", 1m, 100m, 21m, h.CategoryId)], null, null, null, null);

    private static async Task<InvoiceDetailDto> CreateAsync(Harness h, TransportationDbContext? context = null)
    {
        var result = await h.Sut(context).CreateAsync(Request(h), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        return result.Invoice!;
    }

    private static async Task<InvoiceDetailDto> IssueAsync(Harness h, Guid invoiceId, TransportationDbContext? context = null)
    {
        var result = await h.Sut(context).ChangeStatusAsync(invoiceId, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        return result.Invoice!;
    }

    [Fact]
    public async Task TwoIssuesOfTheSameDraft_TheLoserGets409_TheNumberIsIssuedExactlyOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Equal("2026080001", (await IssueAsync(h, (await CreateAsync(h)).Id)).InvoiceNumber); // the series exists
        var draft = await CreateAsync(h);
        h.Db.Context.ChangeTracker.Clear();

        // Colleague B issues the same draft between A's checks and A's claim save.
        h.Race.Arm(
            context => context.ChangeTracker.Entries<Invoice>().Any(e =>
                e.Entity.Id == draft.Id && e.State == EntityState.Modified && e.Entity.Status == InvoiceStatus.Sent),
            async cancellationToken =>
            {
                await using var other = h.Db.CreateContextForTenant(h.TenantId);
                var winner = await h.Sut(other).ChangeStatusAsync(draft.Id, InvoiceStatus.Sent, cancellationToken);
                Assert.Equal(InvoiceOperationOutcome.Success, winner.Outcome);
                Assert.Equal("2026080002", winner.Invoice!.InvoiceNumber);
            });

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            h.Sut().ChangeStatusAsync(draft.Id, InvoiceStatus.Sent, CancellationToken.None));

        Assert.Equal(1, h.Race.Fired);
        Assert.Contains("intussen al uitgereikt", conflict.Message);
        // The invoice keeps the winner's number N; the series advanced exactly once for it.
        var stored = await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == draft.Id);
        Assert.Equal((InvoiceStatus.Sent, "2026080002"), (stored.Status, stored.InvoiceNumber));
        var sequence = await h.Db.Context.InvoiceSequences.AsNoTracking().SingleAsync(s => s.LegalEntityId == h.Entity.Id);
        Assert.Equal(3, sequence.NextValue);
        // Exactly ONE "Issued" (and one "StatusChanged") audit row: the loser wrote nothing.
        var audit = await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Invoice" && a.EntityId == draft.Id.ToString())
            .Select(a => a.Action).ToListAsync();
        Assert.Equal(1, audit.Count(a => a == "Issued"));
        Assert.Equal(1, audit.Count(a => a == "StatusChanged"));

        // A SEQUENTIAL second issue keeps today's answer: a plain 400 on the transition, no conflict.
        h.Db.Context.ChangeTracker.Clear();
        var again = await h.Sut().ChangeStatusAsync(draft.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.InvalidState, again.Outcome);
        Assert.Contains("kan niet naar 'Sent'", again.Error);
    }

    [Fact]
    public async Task EightDifferentDrafts_IssuedInParallel_GetContiguousNumbers_WithoutACrossSeriesError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var workers = Enumerable.Range(0, 8).Select(_ => h.Db.CreateContextForTenant(h.TenantId)).ToList();
        try
        {
            // Every worker first issues one invoice on its own context, so it tracks a (soon stale)
            // sequence row — exactly the production race on the counter.
            foreach (var worker in workers)
            {
                await IssueAsync(h, (await CreateAsync(h, worker)).Id, worker);
            }

            var drafts = new List<InvoiceDetailDto>();
            foreach (var worker in workers)
            {
                drafts.Add(await CreateAsync(h, worker));
            }

            var results = await Task.WhenAll(workers.Select((worker, index) =>
                h.Sut(worker).ChangeStatusAsync(drafts[index].Id, InvoiceStatus.Sent, CancellationToken.None)));

            Assert.All(results, result => Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome));
            var numbers = await h.Db.Context.Invoices.AsNoTracking()
                .Where(i => i.Status == InvoiceStatus.Sent).Select(i => i.InvoiceNumber!).ToListAsync();
            Assert.Equal(16, numbers.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 16).Select(n => $"202608{n:D4}"), numbers.OrderBy(n => n));
            Assert.Equal(17, (await h.Db.Context.InvoiceSequences.AsNoTracking().SingleAsync()).NextValue);
            Assert.Equal(16, await h.Db.Context.AuditLogs.AsNoTracking().CountAsync(a => a.EntityType == "Invoice" && a.Action == "Issued"));
        }
        finally
        {
            workers.ForEach(worker => worker.Dispose());
        }
    }

    /// <summary>The MD-H5 configuration conflict still fires: a number that another SERIES already printed.</summary>
    [Fact]
    public async Task ANumberPrintedByAnotherSeries_IsStillAConfigurationConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Equal("2026080001", (await IssueAsync(h, (await CreateAsync(h)).Id)).InvoiceNumber);

        // A second entity whose format composes the very same strings.
        var clash = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, LegalName = "Acme Clash BV",
            InvoiceNumberFormat = "{YYYY}{MM}{SEQ}", InvoiceSequencePadding = 4, IsActive = true,
        };
        h.Db.Context.LegalEntities.Add(clash);
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();
        var draft = (await h.Sut().CreateAsync(Request(h) with { LegalEntityId = clash.Id }, CancellationToken.None)).Invoice!;

        var error = await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() =>
            h.Sut().ChangeStatusAsync(draft.Id, InvoiceStatus.Sent, CancellationToken.None));
        Assert.Contains("bestaat al in een andere nummerreeks", error.Message);
    }
}
