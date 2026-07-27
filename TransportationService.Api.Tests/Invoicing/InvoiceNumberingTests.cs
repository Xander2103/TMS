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

/// <summary>Entity-scoped monthly invoice numbering (business-feedback wave).</summary>
public class InvoiceNumberingTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero); // August: July is "previous month"

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Sut, Guid TenantId, Guid CustomerId, LegalEntity Entity, LegalEntity SecondEntity);

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

        var tenant = new DevTenantContext(tenantId);
        var sut = new InvoiceService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null))));
        return new Harness(db, sut, tenantId, customerId, entity, secondEntity);
    }

    private static CreateInvoiceRequest Request(Harness h, Guid? entityId = null, int? year = null, int? month = null) => new(
        h.CustomerId, null, [], [new ManualInvoiceLineInput("Transport", 1m, 100m, 21m)], null,
        entityId, year, month);

    [Fact]
    public async Task Create_UsesDefaultEntity_AndMonthPrefixedNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        Assert.Equal("2026080001", result.Invoice!.InvoiceNumber);
        Assert.Equal(h.Entity.Id, result.Invoice.LegalEntityId);
        Assert.Equal(2026, result.Invoice.InvoicePeriodYear);
        Assert.Equal(8, result.Invoice.InvoicePeriodMonth);
    }

    [Fact]
    public async Task Create_SequencesArePerEntityAndPerMonth()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var a1 = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        var a2 = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        var july = await h.Sut.CreateAsync(Request(h, year: 2026, month: 7), CancellationToken.None);
        var other = await h.Sut.CreateAsync(Request(h, entityId: h.SecondEntity.Id), CancellationToken.None);

        Assert.Equal("2026080001", a1.Invoice!.InvoiceNumber);
        Assert.Equal("2026080002", a2.Invoice!.InvoiceNumber);
        // Previous-month invoicing gets the July sequence, independent of August.
        Assert.Equal("2026070001", july.Invoice!.InvoiceNumber);
        // The second entity runs its own sequence and format.
        Assert.Equal("LOG2608-001", other.Invoice!.InvoiceNumber);
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
    public async Task Update_PeriodChange_ReissuesNumber_InNewPeriod_AndNeverReusesOldOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        Assert.Equal("2026080001", created.Invoice!.InvoiceNumber);

        var updated = await h.Sut.UpdateAsync(created.Invoice.Id, new UpdateInvoiceRequest(
            created.Invoice.InvoiceDate, created.Invoice.DueDate,
            [.. created.Invoice.Lines.Select(l => new UpdateInvoiceLineInput(l.Id, l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent))],
            null, 2026, 7), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, updated.Outcome);
        Assert.Equal("2026070001", updated.Invoice!.InvoiceNumber);
        Assert.Equal(7, updated.Invoice.InvoicePeriodMonth);

        // A new August invoice continues after the abandoned number: 0001 was consumed.
        var next = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        Assert.Equal("2026080002", next.Invoice!.InvoiceNumber);
    }

    [Fact]
    public async Task ConcurrentClaim_StaleSequence_RetriesToFreshNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        Assert.Equal("2026080001", first.Invoice!.InvoiceNumber);

        // A "concurrent" request advances the sequence behind the tracked entity's back.
        await h.Db.Context.Database.ExecuteSqlRawAsync(
            "UPDATE invoice_sequences SET \"NextValue\" = 9");

        var second = await h.Sut.CreateAsync(Request(h), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, second.Outcome);
        Assert.Equal("2026080009", second.Invoice!.InvoiceNumber);
        var storedNext = await h.Db.Context.InvoiceSequences
            .Where(s => s.LegalEntityId == h.Entity.Id && s.Month == 8)
            .Select(s => s.NextValue).FirstAsync();
        Assert.Equal(10, storedNext);
    }

    [Fact]
    public async Task CancelAndDelete_NeverReleaseNumbers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(first.Invoice!.Id, InvoiceStatus.Cancelled, CancellationToken.None);
        await h.Sut.DeleteAsync(first.Invoice.Id, CancellationToken.None);

        var next = await h.Sut.CreateAsync(Request(h), CancellationToken.None);

        Assert.Equal("2026080002", next.Invoice!.InvoiceNumber);
    }

    [Fact]
    public async Task OverrideNumber_RequiresReason_BlocksDuplicates_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        var second = await h.Sut.CreateAsync(Request(h), CancellationToken.None);

        var noReason = await h.Sut.OverrideNumberAsync(second.Invoice!.Id,
            new OverrideInvoiceNumberRequest("2026089999", " "), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, noReason.Outcome);

        var duplicate = await h.Sut.OverrideNumberAsync(second.Invoice.Id,
            new OverrideInvoiceNumberRequest(first.Invoice!.InvoiceNumber, "correctie"), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, duplicate.Outcome);

        var ok = await h.Sut.OverrideNumberAsync(second.Invoice.Id,
            new OverrideInvoiceNumberRequest("2026089999", "correctie boekhouding"), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, ok.Outcome);
        Assert.Equal("2026089999", ok.Invoice!.InvoiceNumber);
        Assert.True(ok.Invoice.NumberIsManual);

        var audit = await h.Db.Context.AuditLogs
            .Where(a => a.EntityType == "Invoice" && a.Action == "NumberOverridden")
            .ToListAsync();
        Assert.Single(audit);
        Assert.Contains("correctie boekhouding", audit[0].NewValuesJson);
    }

    [Fact]
    public async Task OverrideNumber_SentInvoice_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(first.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var result = await h.Sut.OverrideNumberAsync(first.Invoice.Id,
            new OverrideInvoiceNumberRequest("X-1", "reden"), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Preview_ShowsNextNumber_WithoutClaiming()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var preview1 = await h.Sut.PreviewNextNumberAsync(null, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080001", preview1!.InvoiceNumber);

        // Preview twice: nothing was claimed.
        var preview2 = await h.Sut.PreviewNextNumberAsync(null, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080001", preview2!.InvoiceNumber);

        await h.Sut.CreateAsync(Request(h), CancellationToken.None);
        var preview3 = await h.Sut.PreviewNextNumberAsync(h.Entity.Id, 2026, 8, CancellationToken.None);
        Assert.Equal("2026080002", preview3!.InvoiceNumber);
    }
}
