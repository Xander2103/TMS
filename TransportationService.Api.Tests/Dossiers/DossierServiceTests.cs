using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

public class DossierServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId, Guid CustomerId, Guid OrderId)
    {
        public DossierService Sut(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var user = new DevCurrentUserContext(UserId);
            return new DossierService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, user), new TestClock(Now));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "p@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-0001",
            CustomerId = customerId,
            OrderDate = new DateOnly(2026, 7, 15),
            AgreedPrice = 500m,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId, customerId, orderId);
    }

    [Fact]
    public async Task Create_ClaimsSequentialDossierNumbers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var first = await sut.CreateAsync(new SaveDossierRequest("Project A", CustomerId: h.CustomerId), CancellationToken.None);
        var second = await sut.CreateAsync(new SaveDossierRequest("Project B", CustomerId: h.CustomerId), CancellationToken.None);

        Assert.Equal("DOS-0001", first.DossierNumber);
        Assert.Equal("DOS-0002", second.DossierNumber);
        Assert.Equal("Open", first.Status);
        Assert.Equal("Klant BV", first.CustomerName);
    }

    [Fact]
    public async Task Create_ValidatesTitleCustomerAndResponsible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        // Fast create (dossier-foundation wave): the customer is the ONLY required field;
        // a blank title falls back to "klant — datum".
        var noCustomer = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(new SaveDossierRequest("  "), CancellationToken.None));
        Assert.Contains("customerId", noCustomer.FieldErrors!.Keys);

        var badCustomer = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(new SaveDossierRequest("X", CustomerId: Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("customerId", badCustomer.FieldErrors!.Keys);

        var badResponsible = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(new SaveDossierRequest("X", CustomerId: h.CustomerId, ResponsibleUserId: Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("responsibleUserId", badResponsible.FieldErrors!.Keys);

        var defaultTitle = await sut.CreateAsync(
            new SaveDossierRequest(null, CustomerId: h.CustomerId, DossierDate: new DateOnly(2026, 8, 12)), CancellationToken.None);
        Assert.Equal("Klant BV — 12-08-2026", defaultTitle.Title);
    }

    [Fact]
    public async Task LinkOrder_AddsOnce_AndFinancialsFollowTheOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Project", CustomerId: h.CustomerId), CancellationToken.None);

        var linked = await sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(h.OrderId), CancellationToken.None);
        var order = Assert.Single(linked!.Orders);
        Assert.Equal("ORD-0001", order.OrderNumber);
        Assert.Equal(500m, linked.Financials.AgreedOrderTotal);

        var duplicate = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(h.OrderId), CancellationToken.None));
        Assert.Contains("transportOrderId", duplicate.FieldErrors!.Keys);

        var unlinked = await sut.UnlinkOrderAsync(dossier.Id, h.OrderId, CancellationToken.None);
        Assert.Empty(unlinked!.Orders);

        // Relinking after unlink works again (soft-deleted link does not block).
        var relinked = await sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(h.OrderId), CancellationToken.None);
        Assert.Single(relinked!.Orders);
    }

    [Fact]
    public async Task LinkOrder_RefusesUnknownOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Project", CustomerId: h.CustomerId), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Relations_RefuseSelfLinksAndDuplicatesInBothDirections()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var first = await sut.CreateAsync(new SaveDossierRequest("Origineel", CustomerId: h.CustomerId), CancellationToken.None);
        var second = await sut.CreateAsync(new SaveDossierRequest("Vervolg", CustomerId: h.CustomerId), CancellationToken.None);

        var self = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.AddRelationAsync(first.Id, new AddDossierRelationRequest(first.Id, "FollowUp"), CancellationToken.None));
        Assert.Contains("targetDossierId", self.FieldErrors!.Keys);

        var linked = await sut.AddRelationAsync(first.Id, new AddDossierRelationRequest(second.Id, "FollowUp", "Vervolgtransport"), CancellationToken.None);
        var relation = Assert.Single(linked!.Relations);
        Assert.True(relation.IsOutgoing);
        Assert.Equal(second.DossierNumber, relation.OtherDossierNumber);

        // Same pair + type is one logical link, whichever side adds it.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.AddRelationAsync(second.Id, new AddDossierRelationRequest(first.Id, "FollowUp"), CancellationToken.None));

        // A different type between the same pair is allowed.
        await sut.AddRelationAsync(second.Id, new AddDossierRelationRequest(first.Id, "Claim"), CancellationToken.None);

        // The far side sees the incoming link too.
        var seenFromSecond = await sut.GetAsync(second.Id, CancellationToken.None);
        Assert.Equal(2, seenFromSecond!.Relations.Count);
        Assert.Contains(seenFromSecond.Relations, r => !r.IsOutgoing && r.RelationType == "FollowUp");

        var removed = await sut.RemoveRelationAsync(first.Id, relation.Id, CancellationToken.None);
        Assert.Single(removed!.Relations);
    }

    [Fact]
    public async Task Close_RefusedWithOpenIncidents_AndClosedDossierIsReadOnly()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Claimdossier", CustomerId: h.CustomerId), CancellationToken.None);

        h.Db.Context.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(),
            TenantId = h.TenantId,
            DossierId = dossier.Id,
            Title = "Schade",
            Description = "Pallet beschadigd",
            Status = IncidentStatus.New,
            EstimatedCost = 250m,
        });
        await h.Db.Context.SaveChangesAsync();

        var blocked = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CloseAsync(dossier.Id, CancellationToken.None));
        Assert.Contains("open incident", blocked.Message);

        var incident = h.Db.Context.Incidents.Single();
        incident.Status = IncidentStatus.Resolved;
        await h.Db.Context.SaveChangesAsync();

        var closed = await sut.CloseAsync(dossier.Id, CancellationToken.None);
        Assert.Equal("Closed", closed!.Status);
        Assert.NotNull(closed.ClosedAt);
        Assert.Equal(250m, closed.Financials.EstimatedIncidentCost);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.UpdateAsync(dossier.Id, new SaveDossierRequest("Nieuwe titel"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(h.OrderId), CancellationToken.None));

        var reopened = await sut.ReopenAsync(dossier.Id, CancellationToken.None);
        Assert.Equal("Open", reopened!.Status);
        Assert.Null(reopened.ClosedAt);
        Assert.NotNull(await sut.UpdateAsync(dossier.Id, new SaveDossierRequest("Nieuwe titel"), CancellationToken.None));
    }

    [Fact]
    public async Task Get_IsTenantScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Sut().CreateAsync(new SaveDossierRequest("Privaat", CustomerId: h.CustomerId), CancellationToken.None);

        Assert.Null(await h.Sut(tenantId: Guid.NewGuid()).GetAsync(dossier.Id, CancellationToken.None));
        Assert.Empty(await h.Sut(tenantId: Guid.NewGuid()).ListAsync(null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task List_FiltersOnStatusSearchAndCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = h.TenantId, CustomerNumber = "KL-2", Name = "Andere BV" });
        await h.Db.Context.SaveChangesAsync();
        await sut.CreateAsync(new SaveDossierRequest("Retourproject", CustomerId: h.CustomerId), CancellationToken.None);
        var toClose = await sut.CreateAsync(new SaveDossierRequest("Afgerond werk", CustomerId: otherCustomerId), CancellationToken.None);
        await sut.CloseAsync(toClose.Id, CancellationToken.None);

        Assert.Equal(2, (await sut.ListAsync(null, null, null, CancellationToken.None)).Count);
        Assert.Single(await sut.ListAsync(null, "Open", null, CancellationToken.None));
        Assert.Single(await sut.ListAsync("retour", null, null, CancellationToken.None));
        Assert.Single(await sut.ListAsync(null, null, h.CustomerId, CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.ListAsync(null, "Nonsense", null, CancellationToken.None));
    }

    // ------------------------------------------------ UX sprint 2026-09-09 §2.5: list contract

    private static async Task<Guid> AddOrderAsync(
        Harness h, string number, decimal? agreedPrice, bool priceIsManual = false,
        OrderPricingSource pricingSource = OrderPricingSource.Contract, decimal? oneOffFixedAmount = null)
    {
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, OrderNumber = number, CustomerId = h.CustomerId,
            OrderDate = new DateOnly(2026, 7, 15), AgreedPrice = agreedPrice, PriceIsManual = priceIsManual,
            PricingSource = pricingSource, OneOffFixedAmount = oneOffFixedAmount,
        };
        h.Db.Context.TransportOrders.Add(order);
        await h.Db.Context.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>
    /// Hardening 2026-09-10: the list, the financials and the per-order flag must all follow
    /// <see cref="OrderPricingState"/> — including a one-off agreement at € 0, which IS a price —
    /// so the three read paths can never drift from the helper (parity across the EF paths).
    /// </summary>
    [Fact]
    public async Task Parity_ListFinancialsAndOrderFlag_FollowOrderPricingState()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var engineZero = await AddOrderAsync(h, "ORD-0002", agreedPrice: 0m);
        var nothing = await AddOrderAsync(h, "ORD-0003", agreedPrice: null);
        var overrideZero = await AddOrderAsync(h, "ORD-0004", agreedPrice: 0m, priceIsManual: true);
        var oneOffZero = await AddOrderAsync(h, "ORD-0005", agreedPrice: 0m, pricingSource: OrderPricingSource.OneOff, oneOffFixedAmount: 0m);
        var oneOffAgreed = await AddOrderAsync(h, "ORD-0006", agreedPrice: null, pricingSource: OrderPricingSource.OneOff, oneOffFixedAmount: 120m);

        var dossier = await sut.CreateAsync(new SaveDossierRequest("Pariteit", CustomerId: h.CustomerId), CancellationToken.None);
        foreach (var orderId in new[] { h.OrderId, engineZero, nothing, overrideZero, oneOffZero, oneOffAgreed })
        {
            await sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(orderId), CancellationToken.None);
        }

        var expected = await h.Db.Context.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == h.TenantId)
            .ToDictionaryAsync(o => o.Id, o => OrderPricingState.IsPriced(o));
        Assert.Equal(4, expected.Count(kv => kv.Value)); // 500, override@0, one-off@0, one-off 120 (not yet derived)

        var row = (await sut.ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(6, row.OrderCount);
        Assert.Equal(4, row.PricedOrderCount);
        Assert.Equal(500m, row.AgreedPriceTotal); // 500 + 0 + 0 + null

        var detail = (await sut.GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(4, detail.Financials.PricedOrderCount);
        Assert.Equal(500m, detail.Financials.AgreedOrderTotal);
        foreach (var order in detail.Orders)
        {
            Assert.Equal(expected[order.OrderId], order.IsPriced);
        }
    }

    [Fact]
    public async Task List_ExposesReferenceCustomerNumberAndPricedTotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var unpriced = await AddOrderAsync(h, "ORD-0002", agreedPrice: 0m);       // engine: no counted line
        var nullPrice = await AddOrderAsync(h, "ORD-0003", agreedPrice: null);
        var overridden = await AddOrderAsync(h, "ORD-0004", agreedPrice: 0m, priceIsManual: true);

        var priced = await sut.CreateAsync(new SaveDossierRequest("Geprijsd", CustomerId: h.CustomerId, CustomerReference: "PO-77"), CancellationToken.None);
        await sut.LinkOrderAsync(priced.Id, new LinkDossierOrderRequest(h.OrderId), CancellationToken.None);   // 500
        await sut.LinkOrderAsync(priced.Id, new LinkDossierOrderRequest(unpriced), CancellationToken.None);    // 0 → not priced
        await sut.LinkOrderAsync(priced.Id, new LinkDossierOrderRequest(overridden), CancellationToken.None);  // override → priced, adds 0

        var notPriced = await sut.CreateAsync(new SaveDossierRequest("Zonder prijs", CustomerId: h.CustomerId), CancellationToken.None);
        await sut.LinkOrderAsync(notPriced.Id, new LinkDossierOrderRequest(nullPrice), CancellationToken.None);

        var empty = await sut.CreateAsync(new SaveDossierRequest("Leeg", CustomerId: h.CustomerId), CancellationToken.None);

        var list = await sut.ListAsync(null, null, null, CancellationToken.None);

        var pricedRow = list.Single(d => d.Id == priced.Id);
        Assert.Equal("PO-77", pricedRow.CustomerReference);
        Assert.Equal("KL-1", pricedRow.CustomerNumber);
        Assert.Equal("Klant BV", pricedRow.CustomerName);
        Assert.Equal(3, pricedRow.OrderCount);
        Assert.Equal(2, pricedRow.PricedOrderCount);
        Assert.Equal(500m, pricedRow.AgreedPriceTotal);

        var notPricedRow = list.Single(d => d.Id == notPriced.Id);
        Assert.Equal(1, notPricedRow.OrderCount);
        Assert.Equal(0, notPricedRow.PricedOrderCount);
        Assert.Null(notPricedRow.AgreedPriceTotal); // "Nog geen prijs", never € 0,00

        var emptyRow = list.Single(d => d.Id == empty.Id);
        Assert.Null(emptyRow.CustomerReference);
        Assert.Equal(0, emptyRow.PricedOrderCount);
        Assert.Null(emptyRow.AgreedPriceTotal);
    }

    [Fact]
    public async Task List_SearchMatchesReferenceCustomerNameAndCustomerNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = h.TenantId, CustomerNumber = "KL-2", Name = "Andere BV" });
        await h.Db.Context.SaveChangesAsync();
        var withRef = await sut.CreateAsync(new SaveDossierRequest("Project Noord", CustomerId: h.CustomerId, CustomerReference: "PO-77"), CancellationToken.None);
        var other = await sut.CreateAsync(new SaveDossierRequest("Project Zuid", CustomerId: otherCustomerId), CancellationToken.None);

        Assert.Equal([withRef.Id], (await sut.ListAsync("po-7", null, null, CancellationToken.None)).Select(d => d.Id));
        Assert.Equal([other.Id], (await sut.ListAsync("andere", null, null, CancellationToken.None)).Select(d => d.Id));
        Assert.Equal([other.Id], (await sut.ListAsync("KL-2", null, null, CancellationToken.None)).Select(d => d.Id));
        Assert.Equal([withRef.Id], (await sut.ListAsync("klant bv", null, null, CancellationToken.None)).Select(d => d.Id));
        Assert.Equal(2, (await sut.ListAsync("project", null, null, CancellationToken.None)).Count);
        Assert.Empty(await sut.ListAsync("bestaat-niet", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Financials_CountPricedOrders_WithTheSameDefinitionAsTheList()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var unpriced = await AddOrderAsync(h, "ORD-0002", agreedPrice: 0m);
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Project", CustomerId: h.CustomerId), CancellationToken.None);
        Assert.Equal(0, dossier.Financials.PricedOrderCount);

        await sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(unpriced), CancellationToken.None);
        var oneUnpriced = await sut.GetAsync(dossier.Id, CancellationToken.None);
        Assert.Equal(0, oneUnpriced!.Financials.PricedOrderCount);
        Assert.Equal(0m, oneUnpriced.Financials.AgreedOrderTotal);

        await sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(h.OrderId), CancellationToken.None);
        var withPriced = await sut.GetAsync(dossier.Id, CancellationToken.None);
        Assert.Equal(1, withPriced!.Financials.PricedOrderCount);
        Assert.Equal(500m, withPriced.Financials.AgreedOrderTotal);
    }
}
