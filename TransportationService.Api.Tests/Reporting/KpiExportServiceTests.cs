using ClosedXML.Excel;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Reporting.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Modules.TripCosting.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reporting;

public class KpiExportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 07, 20);
    private static readonly KpiFilter Filter = new(Monday, Monday.AddDays(4), null, null, null);

    /// <summary>One completed trip for a customer whose name is a spreadsheet-formula payload.</summary>
    private static async Task<(SqliteTestDbContext Db, KpiExportService Sut, Guid TenantId)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1",
            Name = "=HYPERLINK(\"http://evil.example\",\"klik\")", IsActive = true,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = Monday, Status = TransportOrderStatus.Completed, GoodsDescription = "x", AgreedPrice = 1000m,
        });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = Monday,
            Status = TripStatus.Completed, PlannedDistanceKm = 200m, PlannedEmptyKm = 50m,
            Orders = [new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, Sequence = 1 }],
        });
        db.Context.TripCostSummaries.Add(new TripCostSummary
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId,
            EstimatedTotal = 500m, ActualTotal = 800m, ProjectedTotal = 800m, Revenue = 1000m,
            IsFinalized = true, FinalCost = 800m, FinalRevenue = 1000m,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var rates = new CostRateService(db.Context, tenant, audit);
        var queries = new KpiQueryService(db.Context, tenant, rates, clock);
        var sut = new KpiExportService(db.Context, tenant, new DevCurrentUserContext(null), queries, rates, clock);
        return (db, sut, tenantId);
    }

    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content));

    [Fact]
    public async Task TripProfitability_BuildsWorkbook_WithCriteriaAndTypedCells()
    {
        var (db, sut, _) = await SeedAsync();
        using var _1 = db;

        var result = await sut.BuildAsync("trip-profitability", Filter, CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("kpi-trip-profitability-", result.Value.FileName);
        using var workbook = Open(result.Value.Content);
        var sheet = workbook.Worksheet("Ritrendement");
        Assert.Equal("Rit", sheet.Cell(1, 1).GetString());
        Assert.Equal("RIT-0001", sheet.Cell(2, 1).GetString());
        // Numbers are numeric cells with European formats; dates carry dd-MM-yyyy.
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 6).DataType);
        Assert.Equal(1000m, (decimal)sheet.Cell(2, 6).GetDouble());
        Assert.Equal("#,##0.00", sheet.Cell(2, 6).Style.NumberFormat.Format);
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 2).DataType);
        Assert.Equal("dd-MM-yyyy", sheet.Cell(2, 2).Style.DateFormat.Format);

        var criteria = workbook.Worksheet("Criteria");
        var labels = criteria.Column(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Rapport", labels);
        Assert.Contains("Periode van", labels);
        Assert.Contains("Gegenereerd op (UTC)", labels);
        Assert.Equal("20-07-2026", criteria.Cell(2, 2).GetString());
        Assert.Equal("22-07-2026 12:00", criteria.Cell(9, 2).GetString());
    }

    [Fact]
    public async Task FormulaLookingCustomerName_StaysTextNeverFormula()
    {
        var (db, sut, _) = await SeedAsync();
        using var _1 = db;

        var result = await sut.BuildAsync("customer-profitability", Filter, CancellationToken.None);

        using var workbook = Open(result!.Value.Content);
        var cell = workbook.Worksheet("Klantrendement").Cell(2, 1);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.False(cell.HasFormula);
        Assert.Equal("=HYPERLINK(\"http://evil.example\",\"klik\")", cell.GetString());
    }

    [Fact]
    public async Task EveryReportKey_BuildsAWorkbook_UnknownReturnsNull()
    {
        var (db, sut, _) = await SeedAsync();
        using var _1 = db;

        foreach (var key in sut.ReportKeys)
        {
            var result = await sut.BuildAsync(key, Filter, CancellationToken.None);
            Assert.NotNull(result);
            using var workbook = Open(result.Value.Content);
            Assert.Contains(workbook.Worksheets, w => w.Name == "Criteria");
            Assert.Equal(2, workbook.Worksheets.Count);
        }

        Assert.Null(await sut.BuildAsync("nonsense", Filter, CancellationToken.None));
    }

    [Fact]
    public async Task Export_IsTenantIsolated()
    {
        var (db, _, _) = await SeedAsync();
        using var _1 = db;
        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, foreignTenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var rates = new CostRateService(db.Context, foreignTenant, audit);
        var foreign = new KpiExportService(db.Context, foreignTenant, new DevCurrentUserContext(null),
            new KpiQueryService(db.Context, foreignTenant, rates, clock), rates, clock);

        var result = await foreign.BuildAsync("trip-profitability", Filter, CancellationToken.None);

        using var workbook = Open(result!.Value.Content);
        // Header only — no data rows leak across tenants.
        Assert.Equal(1, workbook.Worksheet("Ritrendement").LastRowUsed()!.RowNumber());
    }
}
