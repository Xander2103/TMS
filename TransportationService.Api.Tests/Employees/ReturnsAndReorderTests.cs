using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>Sprint fasen 12-13: retourplicht/uitleningen en bestelvoorstellen.</summary>
public class ReturnsAndReorderTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, IssuedItemService Items, InventoryService Inventory,
        InventoryInsightsService Insights, ReorderProposalService Reorders,
        Guid TenantId, Guid EmployeeId, Guid UserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var notifications = new NotificationService(db.Context, tenant, currentUser, TimeProvider.System);
        var guard = InventoryTestFactory.Guard(currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, guard);
        var items = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory,
            new InventoryTestFactory.AllowAllPermissionService(), guard);
        var insights = new InventoryInsightsService(db.Context, tenant);
        var reorders = new ReorderProposalService(db.Context, tenant, currentUser, audit, notifications, TimeProvider.System);
        return new Harness(db, items, inventory, insights, reorders, tenantId, employeeId, userId);
    }

    private static SaveIssuedItemTemplateRequest Template(
        string name = "Boormachine", bool returnRequired = true, int stock = 5,
        int? target = null, int? reorderQuantity = null, int? warning = null) =>
        new(name, "Gereedschap", null, 1, false, true, ReturnRequired: returnRequired, IsActive: true, SortOrder: 0,
            StockTrackingEnabled: true, Stock: stock,
            LowStockThreshold: warning, MinimumStock: null);

    [Fact]
    public async Task Issue_WithReturnDate_ShowsUpAsLoan_AndOverdueWhenPassed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(), CancellationToken.None);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var nextWeek = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        await h.Items.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            template.Id, null, null, IssuedItemStatus.Issued, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), 1,
            null, null, null, null, ExpectedReturnDate: yesterday, ConditionAtIssue: "Nieuwstaat"), CancellationToken.None);
        await h.Items.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            template.Id, null, null, IssuedItemStatus.Issued, DateOnly.FromDateTime(DateTime.UtcNow), 1,
            null, null, null, null, ExpectedReturnDate: nextWeek), CancellationToken.None);

        var loans = await h.Insights.GetLoansAsync(overdueOnly: false, CancellationToken.None);
        Assert.Equal(2, loans.Count);
        Assert.Equal("Nieuwstaat", loans.First(l => l.IsOverdue).ConditionAtIssue);

        var overdue = await h.Insights.GetLoansAsync(overdueOnly: true, CancellationToken.None);
        Assert.Single(overdue);
        Assert.Equal(yesterday, overdue[0].ExpectedReturnDate);
    }

    [Fact]
    public async Task IssueAndReturn_StampActorsAndPersistDisposition()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(), CancellationToken.None);

        var issued = await h.Items.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            template.Id, null, null, IssuedItemStatus.Issued, DateOnly.FromDateTime(DateTime.UtcNow), 1,
            null, null, null, null), CancellationToken.None);
        Assert.Equal(h.UserId, issued!.IssuedByUserId);

        var returned = await h.Items.UpsertAsync(h.EmployeeId, issued.Id, new SaveEmployeeIssuedItemRequest(
            template.Id, null, null, IssuedItemStatus.Returned, DateOnly.FromDateTime(DateTime.UtcNow), 1,
            null, null, DateOnly.FromDateTime(DateTime.UtcNow), "Kras op behuizing",
            ReturnDisposition: "damaged"), CancellationToken.None);
        Assert.Equal(h.UserId, returned!.ReceivedBackByUserId);
        Assert.Equal("damaged", returned.ReturnDisposition);

        var stored = await h.Db.Context.EmployeeIssuedItems.SingleAsync();
        Assert.Equal("damaged", stored.ReturnDisposition);
    }

    [Fact]
    public async Task ReorderProposal_SuggestsFromTargetAndPackSize_AndBlocksDuplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: 3), CancellationToken.None);
        await h.Inventory.UpdateThresholdsAsync(template.Id,
            new UpdateThresholdsRequest(5, 2, 10, 6, false, true), CancellationToken.None);

        var proposal = await h.Reorders.CreateAsync(new CreateReorderProposalRequest(template.Id), CancellationToken.None);
        // Nodig: 10 − 3 = 7 → afgerond op veelvoud van 6 = 12.
        Assert.Equal(12, proposal.SuggestedQuantity);
        Assert.Equal(3, proposal.CurrentStockSnapshot);
        Assert.Equal(ReorderProposalStatus.Proposed, proposal.Status);

        // Tweede open voorstel voor hetzelfde artikel wordt geweigerd.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Reorders.CreateAsync(new CreateReorderProposalRequest(template.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ReorderProposal_StatusFlow_IsEnforced_AndResolvesNotifications()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: 1), CancellationToken.None);
        await h.Inventory.UpdateThresholdsAsync(template.Id,
            new UpdateThresholdsRequest(null, null, 5, null, false, true), CancellationToken.None);
        var proposal = await h.Reorders.CreateAsync(new CreateReorderProposalRequest(template.Id), CancellationToken.None);

        // Ordered kan niet rechtstreeks vanaf Proposed.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Reorders.ChangeStatusAsync(proposal.Id,
                new ReorderProposalStatusRequest(ReorderProposalStatus.Ordered), CancellationToken.None));

        var approved = await h.Reorders.ChangeStatusAsync(proposal.Id,
            new ReorderProposalStatusRequest(ReorderProposalStatus.Approved, ApprovedQuantity: 6), CancellationToken.None);
        Assert.Equal(6, approved!.ApprovedQuantity);
        Assert.Equal(h.UserId, approved.ApprovedByUserId);

        var ordered = await h.Reorders.ChangeStatusAsync(proposal.Id,
            new ReorderProposalStatusRequest(ReorderProposalStatus.Ordered), CancellationToken.None);
        var completed = await h.Reorders.ChangeStatusAsync(proposal.Id,
            new ReorderProposalStatusRequest(ReorderProposalStatus.Completed), CancellationToken.None);
        Assert.NotNull(completed!.ResolvedAt);

        // Afgerond voorstel is definitief.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Reorders.ChangeStatusAsync(proposal.Id,
                new ReorderProposalStatusRequest(ReorderProposalStatus.Proposed), CancellationToken.None));

        // Na afronding mag een nieuw voorstel voor hetzelfde artikel.
        var next = await h.Reorders.CreateAsync(new CreateReorderProposalRequest(template.Id), CancellationToken.None);
        Assert.Equal(ReorderProposalStatus.Proposed, next.Status);
    }

    [Fact]
    public async Task ReorderProposal_CrossTenantTemplate_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTemplateId = Guid.NewGuid();
        h.Db.Context.IssuedItemTemplates.Add(new IssuedItemTemplate
        {
            Id = foreignTemplateId, TenantId = Guid.NewGuid(), Name = "Vreemd", StockTrackingEnabled = true,
        });
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Reorders.CreateAsync(new CreateReorderProposalRequest(foreignTemplateId), CancellationToken.None));
    }

    [Fact]
    public void SuggestQuantity_RoundsUpToPackSize()
    {
        Assert.Equal(7, IReorderProposalService.SuggestQuantity(3, 10, null));
        Assert.Equal(12, IReorderProposalService.SuggestQuantity(3, 10, 6));
        Assert.Equal(6, IReorderProposalService.SuggestQuantity(10, 10, 6)); // niets nodig → één verpakking
        Assert.Equal(1, IReorderProposalService.SuggestQuantity(10, null, null));
        Assert.Equal(5, IReorderProposalService.SuggestQuantity(-2, 3, null)); // negatieve voorraad telt mee
    }
}
