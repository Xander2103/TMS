using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Incidents;

public class IncidentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId, Guid CustomerId)
    {
        public IncidentService Sut()
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(UserId);
            var clock = new TestClock(Now);
            return new IncidentService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, user),
                new NotificationService(Db.Context, tenant, user, clock),
                clock);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "d@acme.be", FirstName = "Dirk", LastName = "Dispatcher", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId, customerId);
    }

    private static SaveIncidentRequest ValidRequest() => new(
        "Pallet beschadigd", "Bij het lossen is een pallet omgevallen.", "Damage", "High");

    [Fact]
    public async Task Create_ValidatesRequiredFieldsAndEnums()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var noTitle = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { Title = " " }, CancellationToken.None));
        Assert.Contains("title", noTitle.FieldErrors!.Keys);

        var noDescription = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { Description = "" }, CancellationToken.None));
        Assert.Contains("description", noDescription.FieldErrors!.Keys);

        var badType = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { IncidentType = "Nonsense" }, CancellationToken.None));
        Assert.Contains("incidentType", badType.FieldErrors!.Keys);

        var badSeverity = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { Severity = "Extreme" }, CancellationToken.None));
        Assert.Contains("severity", badSeverity.FieldErrors!.Keys);

        // 'Other' needs a name; a named custom type on a known type is dropped.
        var otherWithoutName = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { IncidentType = "Other" }, CancellationToken.None));
        Assert.Contains("customTypeName", otherWithoutName.FieldErrors!.Keys);

        var negativeCost = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { EstimatedCost = -1m }, CancellationToken.None));
        Assert.Contains("estimatedCost", negativeCost.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_RefusesLinksOutsideTheTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var badCustomer = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { CustomerId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Contains("customerId", badCustomer.FieldErrors!.Keys);

        var badOrder = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { TransportOrderId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Contains("transportOrderId", badOrder.FieldErrors!.Keys);

        var badDossier = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidRequest() with { DossierId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Contains("dossierId", badDossier.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_NotifiesTheResponsibleUser()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var incident = await h.Sut().CreateAsync(
            ValidRequest() with { ResponsibleUserId = h.UserId, CustomerId = h.CustomerId }, CancellationToken.None);

        Assert.Equal("New", incident.Status);
        Assert.Equal("Klant BV", incident.CustomerName);
        Assert.Equal("Dirk Dispatcher", incident.ResponsibleName);
        Assert.Contains(h.Db.Context.Notifications, n => n.UserId == h.UserId && n.Type == "incident_assigned");
    }

    [Fact]
    public async Task StatusFlow_ResolveRequiresResolution_ReopenClearsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var incident = await sut.CreateAsync(ValidRequest(), CancellationToken.None);
        Assert.Equal(["InProgress", "Resolved", "Cancelled"], incident.AllowedStatusChanges);

        var started = await sut.ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("InProgress"), CancellationToken.None);
        Assert.Equal("InProgress", started!.Status);

        var noResolution = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("Resolved"), CancellationToken.None));
        Assert.Contains("resolution", noResolution.FieldErrors!.Keys);

        var resolved = await sut.ChangeStatusAsync(incident.Id,
            new ChangeIncidentStatusRequest("Resolved", "Vervangende levering uitgevoerd."), CancellationToken.None);
        Assert.Equal("Resolved", resolved!.Status);
        Assert.Equal("Vervangende levering uitgevoerd.", resolved.Resolution);
        Assert.NotNull(resolved.ResolvedAt);

        // Resolved can only reopen — not be cancelled.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("Cancelled"), CancellationToken.None));

        var reopened = await sut.ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("InProgress"), CancellationToken.None);
        Assert.Equal("InProgress", reopened!.Status);
        Assert.Null(reopened.ResolvedAt);
    }

    [Fact]
    public async Task Update_RefusedWhenCancelled()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var incident = await sut.CreateAsync(ValidRequest(), CancellationToken.None);
        await sut.ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("Cancelled"), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.UpdateAsync(incident.Id, ValidRequest() with { Title = "Aangepast" }, CancellationToken.None));

        // Reactivating makes it editable again.
        await sut.ChangeStatusAsync(incident.Id, new ChangeIncidentStatusRequest("New"), CancellationToken.None);
        var updated = await sut.UpdateAsync(incident.Id, ValidRequest() with { Title = "Aangepast" }, CancellationToken.None);
        Assert.Equal("Aangepast", updated!.Title);
    }

    [Fact]
    public async Task List_FiltersAndComputesOverdue()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(ValidRequest() with { DueDate = new DateOnly(2026, 7, 10) }, CancellationToken.None);
        await sut.CreateAsync(ValidRequest() with
        {
            Title = "Vertraging",
            IncidentType = "Delay",
            Severity = "Low",
            DueDate = new DateOnly(2026, 8, 1),
        }, CancellationToken.None);

        var all = await sut.ListAsync(null, null, null, null, null, CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Single(all, i => i.IsOverdue);

        Assert.Single(await sut.ListAsync(null, null, "High", null, null, CancellationToken.None));
        Assert.Single(await sut.ListAsync("vertraging", null, null, null, null, CancellationToken.None));
        Assert.Equal(2, (await sut.ListAsync(null, "New", null, null, null, CancellationToken.None)).Count);
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.ListAsync(null, null, "Extreme", null, null, CancellationToken.None));
    }
}
