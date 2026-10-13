using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Closure sprint 2026-09-23 (P0) — standalone commercial activities (storage, crane work,
/// plateau …) priced on the dossier flow into the SAME invoice builder as completed orders:
/// candidates per customer, lines traceable to the activity, the pricing record flips to
/// Invoiced, release on cancel/delete/edit mirrors the order rule. Never double: an activity
/// backed by an order is invoiced through that order only; an unpriced activity is never a
/// € 0 line; an explicitly free one is a € 0 line by decision.
/// </summary>
public class ActivityInvoicingTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, InvoiceService Sut, Guid TenantId, Guid CustomerId, Guid DossierId,
        Guid OneOffId, Guid LinesId, Guid FreeId, Guid UnpricedId, Guid OrderBackedId, Guid OtherCustomersId, Guid OtherTenantsId)
    {
        public Task<DossierActivityPricing> Pricing(Guid activityId) =>
            Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync(p => p.DossierActivityId == activityId);

        public Task<IReadOnlyList<UninvoicedActivityDto>> Candidates() =>
            Sut.ListUninvoicedActivitiesAsync(CustomerId, CancellationToken.None);

        public Task<InvoiceOperationResult> Create(params Guid[] activityIds) =>
            Sut.CreateAsync(new CreateInvoiceRequest(CustomerId, new DateOnly(2026, 9, 23), [], [], null, DossierActivityIds: activityIds),
                CancellationToken.None);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var otherDossierId = Guid.NewGuid();
        var foreignDossierId = Guid.NewGuid();
        var standaloneTypeId = Guid.NewGuid();
        var transportTypeId = Guid.NewGuid();

        db.Context.Tenants.AddRange(
            new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime },
            new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme NV", IsActive = true, IsDefault = true });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", VatNumber = "BE0123456789", IsActive = true, DefaultLegalEntityId = entityId },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true, DefaultLegalEntityId = entityId });
        db.Context.ActivityTypes.AddRange(
            new ActivityType { Id = standaloneTypeId, TenantId = tenantId, Code = "PLATEAU", Name = "Plateau", IsActive = true, HasStops = false, IsBillable = true, AllowsDuration = true },
            new ActivityType { Id = transportTypeId, TenantId = tenantId, Code = "DIRECT_TRANSPORT", Name = "Transport", IsActive = true, HasStops = true, IsBillable = true });
        db.Context.TransportDossiers.AddRange(
            new TransportDossier { Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0007", Title = "Werf Noord", CustomerId = customerId, LegalEntityId = entityId, Status = DossierStatus.Open },
            new TransportDossier { Id = otherDossierId, TenantId = tenantId, DossierNumber = "DOS-0008", Title = "Andere", CustomerId = otherCustomerId, LegalEntityId = entityId, Status = DossierStatus.Open },
            new TransportDossier { Id = foreignDossierId, TenantId = otherTenantId, DossierNumber = "DOS-0007", Title = "Elders", CustomerId = customerId, Status = DossierStatus.Open });

        var orderId = Guid.NewGuid();
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 9, 10), Status = TransportOrderStatus.Completed, AgreedPrice = 100m, LegalEntityId = entityId,
        });

        DossierActivity Activity(Guid dossier, Guid typeId, int sequence, string label, Guid? linkedOrder = null, Guid? tenant = null) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenant ?? tenantId, DossierId = dossier, ActivityTypeId = typeId, Sequence = sequence,
            Label = label, PlannedDate = new DateOnly(2026, 9, 15), LinkedTransportOrderId = linkedOrder,
        };
        var oneOff = Activity(dossierId, standaloneTypeId, 1, "Plateau dag 1");
        var lines = Activity(dossierId, standaloneTypeId, 2, "Plateau dag 2");
        var free = Activity(dossierId, standaloneTypeId, 3, "Plateau gratis");
        var unpriced = Activity(dossierId, standaloneTypeId, 4, "Nog te prijzen");
        var orderBacked = Activity(dossierId, transportTypeId, 5, "Transport", linkedOrder: orderId);
        var otherCustomers = Activity(otherDossierId, standaloneTypeId, 1, "Van andere klant");
        var foreign = Activity(foreignDossierId, standaloneTypeId, 1, "Elders", tenant: otherTenantId);
        db.Context.DossierActivities.AddRange(oneOff, lines, free, unpriced, orderBacked, otherCustomers, foreign);

        DossierActivityPricing Pricing(DossierActivity a, ActivityPricingSource source, decimal? fixedAmount, decimal? agreed, bool freeConfirmed = false, Guid? tenant = null) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenant ?? tenantId, DossierActivityId = a.Id, PricingSource = source,
            FixedAmount = fixedAmount, AgreedPrice = agreed, FreeConfirmed = freeConfirmed, Status = OrderPricingStatus.Draft,
        };
        var linesPricing = Pricing(lines, ActivityPricingSource.Lines, null, 330m);
        db.Context.DossierActivityPricings.AddRange(
            Pricing(oneOff, ActivityPricingSource.OneOff, 250m, 250m),
            linesPricing,
            Pricing(free, ActivityPricingSource.Lines, null, 0m, freeConfirmed: true),
            Pricing(orderBacked, ActivityPricingSource.OneOff, 999m, 999m),
            Pricing(otherCustomers, ActivityPricingSource.OneOff, 80m, 80m),
            Pricing(foreign, ActivityPricingSource.OneOff, 70m, 70m, tenant: otherTenantId));
        db.Context.DossierActivityPriceLines.AddRange(
            new DossierActivityPriceLine { Id = Guid.NewGuid(), TenantId = tenantId, DossierActivityPricingId = linesPricing.Id, Sequence = 0, Label = "Plateau", Quantity = 3m, Unit = "uur", UnitPrice = 100m, Amount = 300m },
            new DossierActivityPriceLine { Id = Guid.NewGuid(), TenantId = tenantId, DossierActivityPricingId = linesPricing.Id, Sequence = 1, Label = "Verplaatsing", Quantity = 1m, Unit = null, UnitPrice = 30m, Amount = 30m });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new InvoiceService(db.Context, tenant, audit, new TestClock(Now), new InvoiceNumberService(db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, audit));
        return new Harness(db, sut, tenantId, customerId, dossierId,
            oneOff.Id, lines.Id, free.Id, unpriced.Id, orderBacked.Id, otherCustomers.Id, foreign.Id);
    }

    [Fact]
    public async Task Candidates_ArePricedStandaloneActivities_OfThisCustomer_NotYetInvoiced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var candidates = await h.Candidates();

        Assert.Equal(new HashSet<Guid> { h.OneOffId, h.LinesId, h.FreeId }, candidates.Select(c => c.Id).ToHashSet());
        var oneOff = candidates.Single(c => c.Id == h.OneOffId);
        Assert.Equal(("DOS-0007", "Plateau", 250m, false), (oneOff.DossierNumber, oneOff.ActivityTypeName, oneOff.AgreedPrice, oneOff.IsFree));
        Assert.True(candidates.Single(c => c.Id == h.FreeId).IsFree);
        Assert.Equal(h.DossierId, oneOff.DossierId);
    }

    [Fact]
    public async Task Creating_PutsTraceableLines_AndMarksTheActivitiesInvoiced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Create(h.OneOffId, h.LinesId);

        Assert.True(result.Outcome == InvoiceOperationOutcome.Success, result.Error);
        var lines = result.Invoice!.Lines;
        Assert.Equal(3, lines.Count);

        var oneOffLine = Assert.Single(lines, l => l.DossierActivityId == h.OneOffId);
        Assert.Equal((1m, 250m, "DOS-0007"), (oneOffLine.Quantity, oneOffLine.UnitPrice, oneOffLine.DossierNumber));
        Assert.Contains("Plateau dag 1", oneOffLine.Description);
        Assert.Null(oneOffLine.TransportOrderId);

        var priceLines = lines.Where(l => l.DossierActivityId == h.LinesId).OrderBy(l => l.Sequence).ToList();
        Assert.Equal(2, priceLines.Count);
        Assert.Equal((3m, 100m, "HUR"), (priceLines[0].Quantity, priceLines[0].UnitPrice, priceLines[0].UnitCode));
        Assert.Equal((1m, 30m, "C62"), (priceLines[1].Quantity, priceLines[1].UnitPrice, priceLines[1].UnitCode));
        Assert.Equal(580m, result.Invoice.Subtotal);

        Assert.Equal(OrderPricingStatus.Invoiced, (await h.Pricing(h.OneOffId)).Status);
        Assert.Equal(OrderPricingStatus.Invoiced, (await h.Pricing(h.LinesId)).Status);
        Assert.Equal(OrderPricingStatus.Draft, (await h.Pricing(h.FreeId)).Status);

        // Gone from the candidates; a second invoice for the same activity is refused.
        Assert.Equal([h.FreeId], (await h.Candidates()).Select(c => c.Id));
        var again = await h.Create(h.OneOffId);
        Assert.NotEqual(InvoiceOperationOutcome.Success, again.Outcome);
    }

    [Fact]
    public async Task AnUnconfirmedZeroPrice_IsNotACandidate_UntilConfirmedFree()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var pricing = await h.Db.Context.DossierActivityPricings.SingleAsync(p => p.DossierActivityId == h.OneOffId);
        pricing.FixedAmount = 0m;
        pricing.AgreedPrice = 0m;
        pricing.FreeConfirmed = false;
        await h.Db.Context.SaveChangesAsync();

        // € 0 without the explicit "free" confirmation still carries the pricing.zero warning:
        // it is neither offered nor accepted, so nothing is ever invoiced at € 0 by accident.
        Assert.DoesNotContain(await h.Candidates(), c => c.Id == h.OneOffId);
        Assert.NotEqual(InvoiceOperationOutcome.Success, (await h.Create(h.OneOffId)).Outcome);

        pricing.FreeConfirmed = true;
        await h.Db.Context.SaveChangesAsync();
        Assert.True(Assert.Single(await h.Candidates(), c => c.Id == h.OneOffId).IsFree);
    }

    [Fact]
    public async Task AFreeActivity_IsInvoicedAsAnExplicitZeroLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Create(h.FreeId);

        Assert.True(result.Outcome == InvoiceOperationOutcome.Success, result.Error);
        var line = Assert.Single(result.Invoice!.Lines);
        Assert.Equal((h.FreeId, 0m, 0m), (line.DossierActivityId, line.UnitPrice, result.Invoice.Subtotal));
        Assert.Equal(OrderPricingStatus.Invoiced, (await h.Pricing(h.FreeId)).Status);
    }

    [Fact]
    public async Task UnpricedOrderBackedForeignAndOtherCustomersActivities_AreRefused_NeverAsZero()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        foreach (var id in new[] { h.UnpricedId, h.OrderBackedId, h.OtherCustomersId, h.OtherTenantsId, Guid.NewGuid() })
        {
            var result = await h.Create(id);
            Assert.NotEqual(InvoiceOperationOutcome.Success, result.Outcome);
            Assert.Empty(await h.Db.Context.InvoiceLines.AsNoTracking().Where(l => l.DossierActivityId == id).ToListAsync());
        }

        // The same activity twice in one request is double billing.
        Assert.NotEqual(InvoiceOperationOutcome.Success, (await h.Create(h.OneOffId, h.OneOffId)).Outcome);
        Assert.Empty(await h.Db.Context.Invoices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AnOrderBackedActivity_IsInvoicedThroughItsOrderOnly()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orderId = (await h.Db.Context.DossierActivities.AsNoTracking().SingleAsync(a => a.Id == h.OrderBackedId)).LinkedTransportOrderId!.Value;

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, new DateOnly(2026, 9, 23), [orderId], [], null), CancellationToken.None);

        Assert.True(result.Outcome == InvoiceOperationOutcome.Success, result.Error);
        Assert.All(result.Invoice!.Lines, l => Assert.Null(l.DossierActivityId));
        Assert.Equal(100m, result.Invoice.Subtotal);
        // The activity's own € 999 record stayed untouched and is still not a candidate.
        Assert.Equal(OrderPricingStatus.Draft, (await h.Pricing(h.OrderBackedId)).Status);
        Assert.DoesNotContain(await h.Candidates(), c => c.Id == h.OrderBackedId);
    }

    [Fact]
    public async Task CancellingTheDraft_ReleasesTheActivity_LikeAnOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Create(h.OneOffId);

        var cancelled = await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.True(cancelled.Outcome == InvoiceOperationOutcome.Success, cancelled.Error);
        Assert.Equal(OrderPricingStatus.Locked, (await h.Pricing(h.OneOffId)).Status);
        Assert.Contains(await h.Candidates(), c => c.Id == h.OneOffId);
    }

    [Fact]
    public async Task DeletingTheDraft_ReleasesTheActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Create(h.LinesId);

        var deleted = await h.Sut.DeleteAsync(created.Invoice!.Id, CancellationToken.None);

        Assert.True(deleted.Outcome == InvoiceOperationOutcome.Success, deleted.Error);
        Assert.Equal(OrderPricingStatus.Locked, (await h.Pricing(h.LinesId)).Status);
        Assert.Contains(await h.Candidates(), c => c.Id == h.LinesId);
    }

    [Fact]
    public async Task DroppingTheActivityLines_FromTheDraft_ReleasesTheActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Create(h.OneOffId, h.LinesId);
        var invoice = created.Invoice!;
        var kept = invoice.Lines.Where(l => l.DossierActivityId == h.OneOffId)
            .Select(l => new UpdateInvoiceLineInput(l.Id, l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent, l.SalesCategoryId, l.UnitCode))
            .ToList();

        var updated = await h.Sut.UpdateAsync(invoice.Id,
            new UpdateInvoiceRequest(invoice.InvoiceDate, invoice.DueDate, kept, invoice.Notes), CancellationToken.None);

        Assert.True(updated.Outcome == InvoiceOperationOutcome.Success, updated.Error);
        Assert.Equal(OrderPricingStatus.Locked, (await h.Pricing(h.LinesId)).Status);
        Assert.Equal(OrderPricingStatus.Invoiced, (await h.Pricing(h.OneOffId)).Status);
        Assert.Contains(await h.Candidates(), c => c.Id == h.LinesId);
    }

    [Fact]
    public async Task ASentInvoice_KeepsTheActivityInvoiced_AndBlocksTheDossierCustomerChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Create(h.OneOffId);
        Assert.Equal(InvoiceOperationOutcome.Success, (await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None)).Outcome);

        // The historical document is immutable: cancelling is refused and the activity stays Invoiced.
        Assert.NotEqual(InvoiceOperationOutcome.Success, (await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None)).Outcome);
        Assert.Equal(OrderPricingStatus.Invoiced, (await h.Pricing(h.OneOffId)).Status);
        Assert.DoesNotContain(await h.Candidates(), c => c.Id == h.OneOffId);

        // Moving the dossier to another customer is blocked exactly like an order on a sent invoice.
        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null));
        var orders = new OrderCustomerChangeService(h.Db.Context, tenant, audit);
        var change = new DossierCustomerChangeService(h.Db.Context, tenant, audit, orders, new DossierService(h.Db.Context, tenant, audit, new TestClock(Now)));
        var otherCustomerId = (await h.Db.Context.Customers.AsNoTracking().SingleAsync(c => c.CustomerNumber == "KL-2")).Id;
        var impact = await change.PreviewAsync(h.DossierId, otherCustomerId, CancellationToken.None);
        Assert.NotNull(impact!.BlockedReason);
        Assert.Contains("creditnota", impact.BlockedReason);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            change.ApplyAsync(h.DossierId, new ChangeDossierCustomerRequest(otherCustomerId, "x"), CancellationToken.None));
    }

    [Fact]
    public async Task ADossierCustomerChange_ReleasesActivityLinesFromADraft_AndReopensThePrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Create(h.OneOffId);

        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null));
        var orders = new OrderCustomerChangeService(h.Db.Context, tenant, audit);
        var change = new DossierCustomerChangeService(h.Db.Context, tenant, audit, orders, new DossierService(h.Db.Context, tenant, audit, new TestClock(Now)));
        var otherCustomerId = (await h.Db.Context.Customers.AsNoTracking().SingleAsync(c => c.CustomerNumber == "KL-2")).Id;

        var impact = await change.ApplyAsync(h.DossierId, new ChangeDossierCustomerRequest(otherCustomerId, "Echte klant"), CancellationToken.None);

        Assert.Equal(1, impact!.ActivityInvoiceLinesReleased);
        Assert.Empty(await h.Db.Context.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == created.Invoice!.Id).ToListAsync());
        // The agreed price was made under the OLD customer: back to Draft, to be confirmed anew.
        Assert.Equal(OrderPricingStatus.Draft, (await h.Pricing(h.OneOffId)).Status);
    }
}
