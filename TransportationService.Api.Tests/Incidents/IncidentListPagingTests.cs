using TransportationService.Api.Common.Models;
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

/// <summary>
/// PERF-H3: the incident register is paged and sorted server-side (it used to hand the UI at
/// most 500 rows, which then sorted and paged them itself and reported "500" as the total).
/// </summary>
public class IncidentListPagingTests
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

    private static SaveIncidentRequest Request(string title, string severity, DateOnly? due = null, Guid? customerId = null) =>
        new(title, "Beschrijving.", "Damage", severity, DueDate: due, CustomerId: customerId);

    private static Task<PagedResult<IncidentListItemDto>> ListAsync(IncidentService sut, int? page = null, int? pageSize = null, string? sort = null, string? dir = null) =>
        sut.ListAsync(null, null, null, null, null, CancellationToken.None, page: IncidentListPage.Of(page, pageSize, sort, dir));

    [Fact]
    public async Task List_PagesServerSide_AndReportsTheFullTotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        for (var i = 0; i < 7; i++)
        {
            await sut.CreateAsync(Request($"Incident {i:00}", "Low"), CancellationToken.None);
        }

        var first = await ListAsync(sut, page: 1, pageSize: 3);
        var second = await ListAsync(sut, page: 2, pageSize: 3);
        var third = await ListAsync(sut, page: 3, pageSize: 3);

        Assert.Equal(7, first.TotalCount);
        Assert.Equal(3, first.PageSize);
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Single(third.Items);
        var ids = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList();
        Assert.Equal(7, ids.Distinct().Count());

        // No implicit defaults: pageSize beyond the maximum is clamped, nonsense falls back.
        var huge = await ListAsync(sut, page: 1, pageSize: 1_000_000);
        Assert.Equal(PageRequest.MaxPageSize, huge.PageSize);
        var defaults = await ListAsync(sut, page: 0, pageSize: 0);
        Assert.Equal(1, defaults.Page);
        Assert.Equal(PageRequest.DefaultPageSize, defaults.PageSize);
        // Without any paging argument the service still answers one default page (callers that
        // only need "the incidents about this order" keep working).
        var implicitPage = await sut.ListAsync(null, null, null, null, null, CancellationToken.None);
        Assert.Equal(7, implicitPage.TotalCount);
        Assert.Equal(PageRequest.DefaultPageSize, implicitPage.PageSize);
    }

    [Fact]
    public async Task List_SortsServerSide_OnTheRegisterColumns()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(Request("Bravo", "Critical", due: new DateOnly(2026, 8, 2)), CancellationToken.None);
        await sut.CreateAsync(Request("Alpha", "Low", due: null, customerId: h.CustomerId), CancellationToken.None);
        await sut.CreateAsync(Request("Charlie", "Medium", due: new DateOnly(2026, 8, 1)), CancellationToken.None);

        Assert.Equal(["Alpha", "Bravo", "Charlie"], (await ListAsync(sut, sort: "title", dir: "asc")).Items.Select(i => i.Title));
        Assert.Equal(["Charlie", "Bravo", "Alpha"], (await ListAsync(sut, sort: "title", dir: "desc")).Items.Select(i => i.Title));

        // Severity sorts by rank (Low < Medium < High < Critical), not alphabetically; desc is its natural direction.
        Assert.Equal(["Critical", "Medium", "Low"], (await ListAsync(sut, sort: "severity")).Items.Select(i => i.Severity));
        Assert.Equal(["Low", "Medium", "Critical"], (await ListAsync(sut, sort: "severity", dir: "asc")).Items.Select(i => i.Severity));

        // Due date: earliest first, rows without a due date last in both directions.
        Assert.Equal(["Charlie", "Bravo", "Alpha"], (await ListAsync(sut, sort: "due", dir: "asc")).Items.Select(i => i.Title));
        Assert.Equal(["Bravo", "Charlie", "Alpha"], (await ListAsync(sut, sort: "due", dir: "desc")).Items.Select(i => i.Title));

        // Customer: named customers first, the nameless rows last.
        Assert.Equal("Alpha", (await ListAsync(sut, sort: "customer", dir: "asc")).Items[0].Title);
        Assert.Equal("Alpha", (await ListAsync(sut, sort: "customer", dir: "desc")).Items[0].Title);

        // An unknown key is the default (newest first) and never an error.
        var fallback = await ListAsync(sut, sort: "nonsense", dir: "sideways");
        Assert.Equal(3, fallback.Items.Count);
    }

    [Fact]
    public async Task List_SortPagesAreDisjointAndStable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        // Ten rows with the SAME sort value: the secondary key (created, id) must keep them put.
        for (var i = 0; i < 10; i++)
        {
            await sut.CreateAsync(Request("Zelfde titel", "Medium"), CancellationToken.None);
        }

        var seen = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            var page = await ListAsync(sut, page: pageNumber, pageSize: 3, sort: "title", dir: "asc");
            seen.AddRange(page.Items.Select(i => i.Id));
        }

        Assert.Equal(10, seen.Count);
        Assert.Equal(10, seen.Distinct().Count());
        var again = await ListAsync(sut, page: 2, pageSize: 3, sort: "title", dir: "asc");
        Assert.Equal(seen.Skip(3).Take(3), again.Items.Select(i => i.Id));
    }
}
