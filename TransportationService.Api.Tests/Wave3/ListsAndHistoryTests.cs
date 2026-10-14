using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Wave3;

/// <summary>
/// Wave 3A — B26 server sorting, B50 paged absences, B25 readable audit values (references
/// resolved, enums stored by name).
/// </summary>
public class ListsAndHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ListSort_WhitelistsKeys_AndUsesTheNaturalDirection()
    {
        Assert.Equal(new ListSort("date", true), ListSort.Parse(null, null, "date", ["date", "number"], ["date"]));
        Assert.Equal(new ListSort("number", false), ListSort.Parse("NUMBER", null, "date", ["date", "number"], ["date"]));
        Assert.Equal(new ListSort("number", true), ListSort.Parse("number", "desc", "date", ["date", "number"]));
        // An unknown key (a stale or hand-edited link) falls back instead of failing.
        Assert.Equal(new ListSort("date", true), ListSort.Parse("drop table", "sideways", "date", ["date"], ["date"]));
    }

    [Fact]
    public async Task Absences_SearchPagesAndSorts_OnTheServer()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        var names = new[] { ("An", "Aerts"), ("Bram", "Bosmans"), ("Chris", "Claes") };
        var employees = names.Select((n, i) => new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = $"MED-{i}", FirstName = n.Item1, LastName = n.Item2,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        }).ToList();
        db.Context.Employees.AddRange(employees);
        for (var i = 0; i < 30; i++)
        {
            db.Context.Absences.Add(new Absence
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employees[i % 3].Id, Type = AbsenceType.Vacation,
                StartDate = new DateOnly(2026, 10, 15).AddDays(i), EndDate = new DateOnly(2026, 10, 15).AddDays(i + 1),
                Status = i % 2 == 0 ? AbsenceStatus.Approved : AbsenceStatus.Requested,
            });
        }

        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var sut = new AbsenceService(
            db.Context, tenant, new DevCurrentUserContext(null), new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), new TestClock(Now)),
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-w3", Guid.NewGuid().ToString("N"))),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            new TestClock(Now));

        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 12, 31);
        var page2 = await sut.SearchAsync(from, to, null, null, null, null, null, PageRequest.Of(2, 10), CancellationToken.None);
        Assert.Equal(30, page2.TotalCount);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(new DateOnly(2026, 10, 25), page2.Items[0].StartDate); // start ascending by default

        var byEmployee = await sut.SearchAsync(from, to, null, null, null, "employee", "desc", PageRequest.Of(1, 10), CancellationToken.None);
        Assert.All(byEmployee.Items, a => Assert.Equal("Chris Claes", a.EmployeeName));

        var searched = await sut.SearchAsync(from, to, null, AbsenceStatus.Requested, "bosm", null, null, PageRequest.Of(1, 50), CancellationToken.None);
        Assert.All(searched.Items, a => Assert.Equal("Bram Bosmans", a.EmployeeName));
        Assert.Equal(5, searched.TotalCount);
    }

    [Fact]
    public async Task AuditReferences_ResolveIdsToHumanReferences_TenantScoped_AndEnumsAreStoredByName()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var foreignOrderId = Guid.NewGuid();
        db.Context.Tenants.AddRange(
            new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime },
            new Tenant { Id = otherTenant, Name = "F", Slug = "f", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.AddRange(
            new TransportOrder { Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0024", OrderDate = new(2026, 10, 1), Status = TransportOrderStatus.Draft },
            new TransportOrder { Id = foreignOrderId, TenantId = otherTenant, CustomerId = customerId, OrderNumber = "ORD-9999", OrderDate = new(2026, 10, 1), Status = TransportOrderStatus.Draft });
        await db.Context.SaveChangesAsync();

        var sut = new AuditReferenceResolver(db.Context, new DevTenantContext(tenantId));
        var json = $$"""{"LinkedTransportOrderId":"{{orderId}}","Nested":{"CustomerId":"{{customerId}}"},"Ids":["{{foreignOrderId}}"]}""";
        var labels = await sut.ResolveAsync([json, "not json", null], CancellationToken.None);

        Assert.Equal("ORD-0024", labels[orderId]);
        Assert.Equal("Haven BV", labels[customerId]);
        Assert.False(labels.ContainsKey(foreignOrderId)); // another tenant's id never resolves

        // New audit rows store enums by name ("Planned"), so the history never reads a bare number.
        var tenant = new DevTenantContext(tenantId);
        await new AuditService(db.Context, tenant, new DevCurrentUserContext(null))
            .RecordAsync("TransportOrder", orderId.ToString(), "StatusChanged", new { Status = TransportOrderStatus.Confirmed }, new { Status = TransportOrderStatus.Planned }, CancellationToken.None);
        var row = await db.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "StatusChanged");
        Assert.Contains("\"Planned\"", row.NewValuesJson);
    }
}
