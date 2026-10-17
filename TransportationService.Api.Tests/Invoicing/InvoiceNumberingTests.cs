using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Entity-scoped monthly invoice numbering (business-feedback wave), assigned AT ISSUE (ACC-H3):
/// a draft carries only its concept reference, the legal number is claimed atomically when the
/// invoice is sent, so cancelled or deleted drafts never consume a number and the series has no gaps.
/// </summary>
public class InvoiceNumberingTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero); // August: July is "previous month"

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Sut, Guid TenantId, Guid CustomerId, LegalEntity Entity, LegalEntity SecondEntity, Guid CategoryId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
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
        var secondEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Logistics BV",
            InvoiceNumberFormat = "{PREFIX}{YY}{MM}-{SEQ}", InvoicePrefix = "LOG", InvoiceSequencePadding = 3, IsActive = true,
        };
        db.Context.LegalEntities.AddRange(entity, secondEntity);
        await db.Context.SaveChangesAsync();
        var categoryId = await AccountingTestSeed.TransportCategoryAsync(db.Context, tenantId);

        var tenant = new DevTenantContext(tenantId);
        var sut = new InvoiceService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null))));
        return new Harness(db, sut, tenantId, customerId, entity, secondEntity, categoryId);
    }

    private static CreateInvoiceRequest Request(Harness h, Guid? entityId = null, int? year = null, int? month = null) => new(
        h.CustomerId, null, [], [new ManualInvoiceLineInput("Transport", 1m, 100m, 21m, h.CategoryId)], null,
        entityId, year, month);

    private static async Task<InvoiceDetailDto> CreateAsync(Harness h, Guid? entityId = null, int? year = null, int? month = null)
    {
        var result = await h.Sut.CreateAsync(Request(h, entityId, year, month), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        return result.Invoice!;
    }

    private static async Task<InvoiceDetailDto> IssueAsync(Harness h, Guid invoiceId)
    {
        var result = await h.Sut.ChangeStatusAsync(invoiceId, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        return result.Invoice!;
    }

    [Fact]
    public async Task Create_AssignsNoNumber_Issue_AssignsTheMonthPrefixedNumber_OfTheDefaultEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var draft = await CreateAsync(h);

        // A draft is a concept: no legal number, only a reference derived from its id.
        Assert.Null(draft.InvoiceNumber);
        Assert.StartsWith("CONCEPT-", draft.DisplayNumber);
        Assert.Equal(h.Entity.Id, draft.LegalEntityId);
        Assert.Equal(2026, draft.InvoicePeriodYear);
        Assert.Equal(8, draft.InvoicePeriodMonth);
        Assert.Null(draft.IssuedAt);

        var issued = await IssueAsync(h, draft.Id);

        Assert.Equal("2026080001", issued.InvoiceNumber);
        Assert.Equal("2026080001", issued.DisplayNumber);
        Assert.NotNull(issued.IssuedAt);
    }

    [Fact]
    public async Task Issue_SequencesArePerEntityAndPerMonth()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var a1 = await CreateAsync(h);
        var a2 = await CreateAsync(h);
        var july = await CreateAsync(h, year: 2026, month: 7);
        var other = await CreateAsync(h, entityId: h.SecondEntity.Id);

        Assert.Equal("2026080001", (await IssueAsync(h, a1.Id)).InvoiceNumber);
        Assert.Equal("2026080002", (await IssueAsync(h, a2.Id)).InvoiceNumber);
        // Previous-month invoicing gets the July sequence, independent of August.
        Assert.Equal("2026070001", (await IssueAsync(h, july.Id)).InvoiceNumber);
        // The second entity runs its own sequence and format.
        Assert.Equal("LOG2608-001", (await IssueAsync(h, other.Id)).InvoiceNumber);
    }

    [Fact]
    public async Task Create_FuturePeriod_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h, year: 2026, month: 9), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("toekomst", result.Error);
    }

    [Fact]
    public async Task Create_InactiveOrForeignEntity_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.SecondEntity.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var inactive = await h.Sut.CreateAsync(Request(h, entityId: h.SecondEntity.Id), CancellationToken.None);
        var unknown = await h.Sut.CreateAsync(Request(h, entityId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidReference, inactive.Outcome);
        Assert.Equal(InvoiceOperationOutcome.InvalidReference, unknown.Outcome);
    }

    [Fact]
    public async Task Update_PeriodChange_OnADraft_ClaimsNothing_TheNumberFollowsThePeriodAtIssue()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await CreateAsync(h);
        Assert.Null(created.InvoiceNumber);

        var updated = await h.Sut.UpdateAsync(created.Id, new UpdateInvoiceRequest(
            created.InvoiceDate, created.DueDate,
            [.. created.Lines.Select(l => new UpdateInvoiceLineInput(l.Id, l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent, l.SalesCategoryId))],
            null, 2026, 7), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, updated.Outcome);
        Assert.Null(updated.Invoice!.InvoiceNumber);
        Assert.Equal(7, updated.Invoice.InvoicePeriodMonth);
        Assert.False(await h.Db.Context.InvoiceSequences.AnyAsync());

        // Issued in July: the July series. The abandoned August draft period consumed nothing, so
        // the next August invoice is still 0001 — no gap.
        Assert.Equal("2026070001", (await IssueAsync(h, created.Id)).InvoiceNumber);
        var next = await CreateAsync(h);
        Assert.Equal("2026080001", (await IssueAsync(h, next.Id)).InvoiceNumber);
    }

    [Fact]
    public async Task ConcurrentClaim_StaleSequence_RetriesToFreshNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await CreateAsync(h);
        Assert.Equal("2026080001", (await IssueAsync(h, first.Id)).InvoiceNumber);

        // A "concurrent" request advances the sequence behind the tracked entity's back.
        await h.Db.Context.Database.ExecuteSqlRawAsync(
            "UPDATE invoice_sequences SET \"NextValue\" = 9");

        var second = await CreateAsync(h);
        var issued = await IssueAsync(h, second.Id);

        Assert.Equal("2026080009", issued.InvoiceNumber);
        var storedNext = await h.Db.Context.InvoiceSequences
            .Where(s => s.LegalEntityId == h.Entity.Id && s.Month == 8)
            .Select(s => s.NextValue).FirstAsync();
        Assert.Equal(10, storedNext);
    }

    [Fact]
    public async Task DraftCancelAndDelete_ConsumeNoNumber_AndTheSeriesHasNoGaps()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var abandoned = await CreateAsync(h);
        await h.Sut.ChangeStatusAsync(abandoned.Id, InvoiceStatus.Cancelled, CancellationToken.None);
        await h.Sut.DeleteAsync(abandoned.Id, CancellationToken.None);

        // Nothing was claimed by the draft: the first issued invoice of the month is 0001.
        var first = await CreateAsync(h);
        Assert.Equal("2026080001", (await IssueAsync(h, first.Id)).InvoiceNumber);
        var second = await CreateAsync(h);
        Assert.Equal("2026080002", (await IssueAsync(h, second.Id)).InvoiceNumber);

        // An issued number is final: the series only moves forward.
        var sequence = await h.Db.Context.InvoiceSequences.SingleAsync(s => s.LegalEntityId == h.Entity.Id);
        Assert.Equal(3, sequence.NextValue);
        Assert.Equal(InvoiceKind.Invoice, sequence.Kind);
    }

    [Fact]
    public async Task OverrideNumber_RequiresReason_BlocksDuplicates_AndAudits_AndIsKeptAtIssue()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await IssueAsync(h, (await CreateAsync(h)).Id);
        var second = await CreateAsync(h);

        var noReason = await h.Sut.OverrideNumberAsync(second.Id,
            new OverrideInvoiceNumberRequest("2026089999", " "), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, noReason.Outcome);

        var duplicate = await h.Sut.OverrideNumberAsync(second.Id,
            new OverrideInvoiceNumberRequest(first.InvoiceNumber!, "correctie"), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, duplicate.Outcome);

        var ok = await h.Sut.OverrideNumberAsync(second.Id,
            new OverrideInvoiceNumberRequest("2026089999", "correctie boekhouding"), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, ok.Outcome);
        Assert.Equal("2026089999", ok.Invoice!.InvoiceNumber);
        Assert.True(ok.Invoice.NumberIsManual);

        var audit = await h.Db.Context.AuditLogs
            .Where(a => a.EntityType == "Invoice" && a.Action == "NumberOverridden")
            .ToListAsync();
        Assert.Single(audit);
        Assert.Contains("correctie boekhouding", audit[0].NewValuesJson);

        // The manual number survives the issue step; the series is not advanced for it.
        Assert.Equal("2026089999", (await IssueAsync(h, second.Id)).InvoiceNumber);
        var sequence = await h.Db.Context.InvoiceSequences.SingleAsync(s => s.LegalEntityId == h.Entity.Id);
        Assert.Equal(2, sequence.NextValue);
    }

    [Fact]
    public async Task OverrideNumber_SentInvoice_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await IssueAsync(h, (await CreateAsync(h)).Id);

        var result = await h.Sut.OverrideNumberAsync(first.Id,
            new OverrideInvoiceNumberRequest("X-1", "reden"), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Preview_ShowsNextNumber_WithoutClaiming_DraftsDoNotMoveIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var preview1 = await h.Sut.PreviewNextNumberAsync(null, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080001", preview1!.InvoiceNumber);

        // Preview twice: nothing was claimed.
        var preview2 = await h.Sut.PreviewNextNumberAsync(null, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080001", preview2!.InvoiceNumber);

        // A draft claims nothing either; only the issue step moves the series.
        var draft = await CreateAsync(h);
        var preview3 = await h.Sut.PreviewNextNumberAsync(h.Entity.Id, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080001", preview3!.InvoiceNumber);

        await IssueAsync(h, draft.Id);
        var preview4 = await h.Sut.PreviewNextNumberAsync(h.Entity.Id, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080002", preview4!.InvoiceNumber);
    }
}
