using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Auditing;

/// <summary>
/// PERF-H5: <c>GET /api/audit-logs?pageSize=1000000</c> used to return the whole tenant log
/// (211k rows, 97 MB). The reader now clamps every page request through <see cref="PageRequest"/>
/// and pages on a stable key, so no page skips or repeats a row.
/// </summary>
public class AuditHistoryReaderPagingTests
{
    private static readonly DateTime Stamp = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId)
    {
        public AuditHistoryReader Reader()
        {
            var tenant = new DevTenantContext(TenantId);
            return new AuditHistoryReader(Db.Context, tenant, new AuditReferenceResolver(Db.Context, tenant));
        }
    }

    private static async Task<Harness> SeedAsync(int rows, bool sameTimestamp)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Stamp });
        for (var i = 0; i < rows; i++)
        {
            db.Context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityType = "Customer",
                EntityId = $"c-{i}",
                Action = "Updated",
                Timestamp = sameTimestamp ? Stamp : Stamp.AddSeconds(i),
            });
        }

        // Another tenant's rows never count, whatever the page size.
        var other = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = other, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Stamp });
        db.Context.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), TenantId = other, EntityType = "Customer", EntityId = "x", Action = "Created", Timestamp = Stamp });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId);
    }

    [Fact]
    public async Task HugePageSize_IsClampedToMaxPageSize_TotalCountStaysComplete()
    {
        var h = await SeedAsync(rows: 230, sameTimestamp: false);
        using var _ = h.Db;

        var page = await h.Reader().ReadAsync(h.Db.Context.AuditLogs, page: 1, pageSize: 1_000_000, redactedKeys: null, CancellationToken.None);

        Assert.Equal(PageRequest.MaxPageSize, page.PageSize);
        Assert.Equal(200, page.Items.Count);
        Assert.Equal(230, page.TotalCount);
        Assert.Equal(1, page.Page);
        // Newest first.
        Assert.Equal(Stamp.AddSeconds(229), page.Items[0].Timestamp);
    }

    [Fact]
    public async Task NonPositivePaging_FallsBackToDefaults()
    {
        var h = await SeedAsync(rows: 30, sameTimestamp: false);
        using var _ = h.Db;

        var page = await h.Reader().ReadAsync(h.Db.Context.AuditLogs, page: 0, pageSize: 0, redactedKeys: null, CancellationToken.None);

        Assert.Equal(1, page.Page);
        Assert.Equal(PageRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(25, page.Items.Count);
        Assert.Equal(30, page.TotalCount);

        var negative = await h.Reader().ReadAsync(h.Db.Context.AuditLogs, page: -3, pageSize: -1, redactedKeys: null, CancellationToken.None);
        Assert.Equal(1, negative.Page);
        Assert.Equal(PageRequest.DefaultPageSize, negative.PageSize);
    }

    [Fact]
    public async Task PagesAreStable_WhenRowsShareTheSameTimestamp()
    {
        // One SaveChanges writes many rows with one timestamp; paging on Timestamp alone made
        // their order undefined between pages (rows skipped on one page, repeated on the next).
        var h = await SeedAsync(rows: 30, sameTimestamp: true);
        using var _ = h.Db;
        var reader = h.Reader();

        var seen = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await reader.ReadAsync(h.Db.Context.AuditLogs, pageNumber, 10, redactedKeys: null, CancellationToken.None);
            Assert.Equal(10, page.Items.Count);
            Assert.Equal(30, page.TotalCount);
            seen.AddRange(page.Items.Select(i => i.Id));
        }

        Assert.Equal(30, seen.Distinct().Count());
        // Reading the same page twice gives the same rows in the same order.
        var again = await reader.ReadAsync(h.Db.Context.AuditLogs, 2, 10, redactedKeys: null, CancellationToken.None);
        Assert.Equal(seen.Skip(10).Take(10), again.Items.Select(i => i.Id));
    }
}
