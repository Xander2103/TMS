using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Follow-up wave P1-P3: the document strategy — order override beats customer default beats
/// tenant rules beats built-in reference defaults; batches (trip and customer/day) respect it.
/// </summary>
public class DocumentStrategyTests
{
    // --- Resolver unit tests (P2 rule priority + overrides) ---

    private static TenantDocumentRule Rule(int priority, bool? crossBorder, bool? adr, string kind, Guid? activityTypeId = null) =>
        new() { Id = Guid.NewGuid(), Priority = priority, MatchCrossBorder = crossBorder, MatchAdr = adr, MatchActivityTypeId = activityTypeId, DocumentKind = kind };

    [Fact]
    public void Resolver_BuiltInDefaults_AdrAndCrossBorderYieldCmr_DomesticYieldsDeliveryNote()
    {
        var domestic = DocumentStrategyResolver.Resolve(null, "GenerateOwn", crossBorder: false, adrRequired: false, null, []);
        Assert.Equal("DeliveryNote", domestic.Kind);
        Assert.Equal("BuiltInDefault", domestic.Source);

        var adr = DocumentStrategyResolver.Resolve(null, "GenerateOwn", crossBorder: false, adrRequired: true, null, []);
        Assert.Equal("Cmr", adr.Kind);

        var foreign = DocumentStrategyResolver.Resolve(null, "GenerateOwn", crossBorder: true, adrRequired: false, null, []);
        Assert.Equal("Cmr", foreign.Kind);
    }

    [Fact]
    public void Resolver_TenantRules_FirstFullMatchByPriorityWins()
    {
        var rules = new[]
        {
            Rule(10, crossBorder: false, adr: false, "DeliveryNote"),
            Rule(20, crossBorder: null, adr: true, "Cmr"),
            Rule(30, crossBorder: null, adr: null, "None"),
        };

        var domestic = DocumentStrategyResolver.Resolve(null, "GenerateOwn", false, false, null, rules);
        Assert.Equal("DeliveryNote", domestic.Kind);
        Assert.Equal("TenantRule", domestic.Source);

        var adr = DocumentStrategyResolver.Resolve(null, "GenerateOwn", true, true, null, rules);
        Assert.Equal("Cmr", adr.Kind);

        // Cross-border non-ADR falls through to the catch-all "None" rule.
        var foreign = DocumentStrategyResolver.Resolve(null, "GenerateOwn", true, false, null, rules);
        Assert.True(foreign.NoneRequired);
        Assert.Null(foreign.Kind);
    }

    [Fact]
    public void Resolver_ActivityTypeRule_OnlyMatchesThatActivity()
    {
        var direct = Guid.NewGuid();
        var rules = new[] { Rule(10, null, null, "Cmr", activityTypeId: direct) };

        var matching = DocumentStrategyResolver.Resolve(null, "GenerateOwn", false, false, direct, rules);
        Assert.Equal("Cmr", matching.Kind);
        Assert.Equal("TenantRule", matching.Source);

        var other = DocumentStrategyResolver.Resolve(null, "GenerateOwn", false, false, Guid.NewGuid(), rules);
        Assert.Equal("DeliveryNote", other.Kind);
        Assert.Equal("BuiltInDefault", other.Source);
    }

    [Fact]
    public void Resolver_Precedence_OrderOverrideBeatsCustomerDefaultBeatsRules()
    {
        var rules = new[] { Rule(10, null, null, "Cmr") };

        // Customer says "customer document" — but the order explicitly chose our own doc.
        var own = DocumentStrategyResolver.Resolve("Own", "CustomerDocument", false, false, null, rules);
        Assert.Equal("Cmr", own.Kind);
        Assert.Equal("OrderOverride", own.Source);
        Assert.True(own.GeneratesOwnDocument);

        // Customer generates own — but the order says customer document.
        var customerDoc = DocumentStrategyResolver.Resolve("CustomerDocument", "GenerateOwn", false, false, null, rules);
        Assert.True(customerDoc.UsesCustomerDocument);
        Assert.False(customerDoc.GeneratesOwnDocument);

        var none = DocumentStrategyResolver.Resolve("NoneRequired", "GenerateOwn", false, false, null, rules);
        Assert.True(none.NoneRequired);

        // Customer default without order preference.
        var inherited = DocumentStrategyResolver.Resolve(null, "CustomerDocument", false, false, null, rules);
        Assert.True(inherited.UsesCustomerDocument);
        Assert.Equal("CustomerDefault", inherited.Source);

        // PerOrder without a choice = undecided, with a suggestion.
        var undecided = DocumentStrategyResolver.Resolve(null, "PerOrder", false, false, null, rules);
        Assert.True(undecided.Undecided);
        Assert.Equal("Cmr", undecided.Kind);
    }

    // --- Service tests (P1 batch exclusion + P3 customer/day) ---

    private sealed record Harness(SqliteTestDbContext Db, TransportDocumentService Sut, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync(string customerStrategy = "GenerateOwn")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            DocumentStrategy = customerStrategy, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new TransportDocumentService(db.Context, new DevTenantContext(tenantId)), tenantId, customerId);
    }

    private static TransportOrder Order(Harness h, string number, string? preference = null, DateOnly? orderDate = null)
    {
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            OrderNumber = number, OrderDate = orderDate ?? new DateOnly(2026, 8, 13),
            Status = TransportOrderStatus.Confirmed,
            GoodsDescription = "Paletten", Quantity = 5, DocumentPreference = preference,
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, StopType = StopType.Loading,
            City = "Antwerpen", CountryCode = "BE",
        });
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 2, StopType = StopType.Unloading,
            City = "Hasselt", CountryCode = "BE",
        });
        h.Db.Context.TransportOrders.Add(order);
        return order;
    }

    private static int PageCount(byte[] pdfBytes)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(
            new MemoryStream(pdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Fact]
    public async Task TripBatch_SkipsCustomerDocumentAndNoneRequiredOrders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var own = Order(h, "ORD-1");
        var customerDoc = Order(h, "ORD-2", preference: "CustomerDocument");
        var none = Order(h, "ORD-3", preference: "NoneRequired");
        var trip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0001",
            TripDate = new DateOnly(2026, 8, 13), Status = TripStatus.Planned,
        };
        h.Db.Context.Trips.Add(trip);
        h.Db.Context.TripOrders.AddRange(
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = own.Id, Sequence = 1 },
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = customerDoc.Id, Sequence = 2 },
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = none.Id, Sequence = 3 });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RenderTripBatchAsync(trip.Id, "delivery-note", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, PageCount(result!.Value.Content));
    }

    [Fact]
    public async Task CustomerDayPreview_CountsOwnCustomerAndUndecidedDocuments()
    {
        var h = await SeedAsync(customerStrategy: "PerOrder");
        using var _ = h.Db;
        Order(h, "ORD-1", preference: "Own");                // eigen leveringsbon
        Order(h, "ORD-2", preference: "CustomerDocument");   // klantdocument
        Order(h, "ORD-3");                                    // nog te beslissen
        Order(h, "ORD-9", orderDate: new DateOnly(2026, 8, 20)); // andere dag → niet in preview
        await h.Db.Context.SaveChangesAsync();

        var preview = await h.Sut.PreviewCustomerDayAsync(
            h.CustomerId, new DateOnly(2026, 8, 13), CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(3, preview!.TotalOrders);
        Assert.Equal(1, preview.OwnDeliveryNotes);
        Assert.Equal(1, preview.CustomerDocuments);
        Assert.Equal(1, preview.Undecided);
        Assert.Contains(preview.Rows, r => r.OrderNumber == "ORD-2" && r.UsesCustomerDocument);
    }

    [Fact]
    public async Task CustomerDayBatch_RendersOnlyApplicableOwnDocumentsOfTheKind()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Order(h, "ORD-1");                                    // leveringsbon (binnenlands)
        Order(h, "ORD-2", preference: "CustomerDocument");   // uitgesloten
        var foreign = Order(h, "ORD-3");                      // CMR (grensoverschrijdend)
        foreign.Stops.Last().CountryCode = "FR";
        await h.Db.Context.SaveChangesAsync();

        var notes = await h.Sut.RenderCustomerDayBatchAsync(
            h.CustomerId, new DateOnly(2026, 8, 13), "delivery-note", null, CancellationToken.None);
        Assert.NotNull(notes);
        Assert.Equal(1, PageCount(notes!.Value.Content));
        Assert.StartsWith("leveringsbonnen-KL-1-20260813", notes.Value.FileName);

        var cmrs = await h.Sut.RenderCustomerDayBatchAsync(
            h.CustomerId, new DateOnly(2026, 8, 13), "cmr", null, CancellationToken.None);
        Assert.NotNull(cmrs);
        Assert.Equal(1, PageCount(cmrs!.Value.Content));
    }

    [Fact]
    public async Task GetStrategy_ExplainsTheDecisionForTheOrder()
    {
        var h = await SeedAsync(customerStrategy: "CustomerDocument");
        using var _ = h.Db;
        var order = Order(h, "ORD-1");
        await h.Db.Context.SaveChangesAsync();

        var strategy = await h.Sut.GetStrategyAsync(order.Id, CancellationToken.None);

        Assert.NotNull(strategy);
        Assert.True(strategy!.UsesCustomerDocument);
        Assert.Equal("CustomerDefault", strategy.Source);
        Assert.Equal("CustomerDocument", strategy.CustomerStrategy);
        Assert.Contains("klant", strategy.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
