using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Orders.Entities;
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
        var second = await sut.CreateAsync(new SaveDossierRequest("Project B"), CancellationToken.None);

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

        var noTitle = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(new SaveDossierRequest("  "), CancellationToken.None));
        Assert.Contains("title", noTitle.FieldErrors!.Keys);

        var badCustomer = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(new SaveDossierRequest("X", CustomerId: Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("customerId", badCustomer.FieldErrors!.Keys);

        var badResponsible = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(new SaveDossierRequest("X", ResponsibleUserId: Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("responsibleUserId", badResponsible.FieldErrors!.Keys);
    }

    [Fact]
    public async Task LinkOrder_AddsOnce_AndFinancialsFollowTheOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Project"), CancellationToken.None);

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
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Project"), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.LinkOrderAsync(dossier.Id, new LinkDossierOrderRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Relations_RefuseSelfLinksAndDuplicatesInBothDirections()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var first = await sut.CreateAsync(new SaveDossierRequest("Origineel"), CancellationToken.None);
        var second = await sut.CreateAsync(new SaveDossierRequest("Vervolg"), CancellationToken.None);

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
        var dossier = await sut.CreateAsync(new SaveDossierRequest("Claimdossier"), CancellationToken.None);

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
        var dossier = await h.Sut().CreateAsync(new SaveDossierRequest("Privaat"), CancellationToken.None);

        Assert.Null(await h.Sut(tenantId: Guid.NewGuid()).GetAsync(dossier.Id, CancellationToken.None));
        Assert.Empty(await h.Sut(tenantId: Guid.NewGuid()).ListAsync(null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task List_FiltersOnStatusSearchAndCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(new SaveDossierRequest("Retourproject", CustomerId: h.CustomerId), CancellationToken.None);
        var toClose = await sut.CreateAsync(new SaveDossierRequest("Afgerond werk"), CancellationToken.None);
        await sut.CloseAsync(toClose.Id, CancellationToken.None);

        Assert.Equal(2, (await sut.ListAsync(null, null, null, CancellationToken.None)).Count);
        Assert.Single(await sut.ListAsync(null, "Open", null, CancellationToken.None));
        Assert.Single(await sut.ListAsync("retour", null, null, CancellationToken.None));
        Assert.Single(await sut.ListAsync(null, null, h.CustomerId, CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.ListAsync(null, "Nonsense", null, CancellationToken.None));
    }
}
