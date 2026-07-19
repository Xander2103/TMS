using System.Text.Json;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Edi.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Edi;

/// <summary>
/// Wave 11: provider-neutral EDI — inbound orders through the generic JSON profile with
/// partner/location mapping, duplicate detection, validation errors, replay, dead-letter and
/// outbound status payloads. No partner-specific format is implemented (none specified).
/// </summary>
public class EdiServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, EdiService Sut, Guid TenantId, Guid PartnerId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partnerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1 });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "TERM-L", Name = "Terminal Links", City = "Antwerpen", IsActive = true,
        });
        db.Context.TradingPartners.Add(new TradingPartner
        {
            Id = partnerId, TenantId = tenantId, Code = "haven-edi", Name = "Haven EDI",
            CustomerId = customerId, MappingProfile = "generic-json-v1", IsActive = true,
        });
        db.Context.EdiPartnerLocations.Add(new EdiPartnerLocation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TradingPartnerId = partnerId,
            ExternalLocationCode = "TERMINAL-77", LocationId = locationId,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, user);
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now));
        var sut = new EdiService(db.Context, tenant, audit, orders, new TestClock(Now));
        return new Harness(db, sut, tenantId, partnerId, customerId, locationId);
    }

    private static string OrderPayload(string externalId = "EXT-1001") => JsonSerializer.Serialize(new
    {
        externalOrderId = externalId,
        customerReference = "PO-9",
        goodsDescription = "20 paletten via EDI",
        stops = new object[]
        {
            new { type = "Loading", externalLocationCode = "TERMINAL-77" },
            new { type = "Unloading", city = "Gent" },
        },
        cargoItems = new object[]
        {
            new { description = "Pallet", barcode = "EDI-BC-1", quantity = 20 },
        },
    });

    [Fact]
    public async Task Ingest_ParsesMapsAndCreatesOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.IngestAsync("haven-edi", "order", OrderPayload(), CancellationToken.None);

        Assert.Equal(EdiProcessingStatus.Processed, result.Message!.Status);
        Assert.NotNull(result.Message.ResultEntityId);

        var order = h.Db.Context.TransportOrders.Single();
        Assert.Equal(h.CustomerId, order.CustomerId);
        Assert.Equal("PO-9", order.CustomerReference);
        var stops = h.Db.Context.TransportOrderStops.OrderBy(s => s.Sequence).ToList();
        Assert.Equal(h.LocationId, stops[0].LocationId);
        Assert.Equal("Gent", stops[1].City);
        Assert.Single(h.Db.Context.CargoItems.Where(c => c.Barcode == "EDI-BC-1"));
    }

    [Fact]
    public async Task Ingest_DetectsDuplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.IngestAsync("haven-edi", "order", OrderPayload(), CancellationToken.None);

        var duplicate = await h.Sut.IngestAsync("haven-edi", "order", OrderPayload(), CancellationToken.None);

        Assert.Equal(EdiProcessingStatus.Duplicate, duplicate.Message!.Status);
        Assert.Single(h.Db.Context.TransportOrders);
    }

    [Fact]
    public async Task Ingest_UnknownPartner_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.IngestAsync("bestaat-niet", "order", OrderPayload(), CancellationToken.None);

        Assert.Equal(EdiIngestOutcome.UnknownPartner, result.Outcome);
    }

    [Fact]
    public async Task Ingest_InvalidPayload_FailsWithValidationDetail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var missingStops = JsonSerializer.Serialize(new { externalOrderId = "EXT-X", goodsDescription = "leeg" });
        var result = await h.Sut.IngestAsync("haven-edi", "order", missingStops, CancellationToken.None);

        Assert.Equal(EdiProcessingStatus.Failed, result.Message!.Status);
        Assert.NotNull(result.Message.ErrorDetail);
        Assert.Empty(h.Db.Context.TransportOrders);
    }

    [Fact]
    public async Task Replay_AfterFix_Processes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Unknown external location fails validation first.
        var payload = OrderPayload().Replace("TERMINAL-77", "ONBEKEND-99");
        var failed = await h.Sut.IngestAsync("haven-edi", "order", payload, CancellationToken.None);
        Assert.Equal(EdiProcessingStatus.Failed, failed.Message!.Status);

        // Operator adds the mapping, then replays.
        h.Db.Context.EdiPartnerLocations.Add(new EdiPartnerLocation
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TradingPartnerId = h.PartnerId,
            ExternalLocationCode = "ONBEKEND-99", LocationId = h.LocationId,
        });
        await h.Db.Context.SaveChangesAsync();

        var replayed = await h.Sut.ReplayAsync(failed.Message.Id, CancellationToken.None);

        Assert.Equal(EdiProcessingStatus.Processed, replayed!.Status);
        Assert.Single(h.Db.Context.TransportOrders);
    }

    [Fact]
    public async Task RepeatedFailures_DeadLetter()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var payload = OrderPayload().Replace("TERMINAL-77", "ONBEKEND-99");
        var failed = await h.Sut.IngestAsync("haven-edi", "order", payload, CancellationToken.None);

        await h.Sut.ReplayAsync(failed.Message!.Id, CancellationToken.None);
        var third = await h.Sut.ReplayAsync(failed.Message.Id, CancellationToken.None);

        Assert.Equal(EdiProcessingStatus.DeadLettered, third!.Status);

        // Dead-lettered messages still replay manually after a fix.
        h.Db.Context.EdiPartnerLocations.Add(new EdiPartnerLocation
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TradingPartnerId = h.PartnerId,
            ExternalLocationCode = "ONBEKEND-99", LocationId = h.LocationId,
        });
        await h.Db.Context.SaveChangesAsync();
        var revived = await h.Sut.ReplayAsync(failed.Message.Id, CancellationToken.None);
        Assert.Equal(EdiProcessingStatus.Processed, revived!.Status);
    }

    [Fact]
    public async Task OutboundStatus_QueuedForEdiOrders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var ingested = await h.Sut.IngestAsync("haven-edi", "order", OrderPayload(), CancellationToken.None);
        var orderId = Guid.Parse(ingested.Message!.ResultEntityId!);

        await h.Sut.QueueOutboundStatusAsync(orderId, "Confirmed", CancellationToken.None);

        var outbound = h.Db.Context.EdiMessages.Single(m => m.Direction == EdiDirection.Outbound);
        Assert.Equal(EdiProcessingStatus.Processed, outbound.Status);
        Assert.Contains("EXT-1001", outbound.PayloadJson);
        Assert.Contains("Confirmed", outbound.PayloadJson);
    }

    [Fact]
    public async Task TenantIsolation_ForeignTenantSeesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var ingested = await h.Sut.IngestAsync("haven-edi", "order", OrderPayload(), CancellationToken.None);

        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreignAudit = new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null));
        var foreign = new EdiService(h.Db.Context, foreignTenant, foreignAudit,
            new TransportOrderService(h.Db.Context, foreignTenant, foreignAudit, new TestClock(Now)),
            new TestClock(Now));

        Assert.Equal(EdiIngestOutcome.UnknownPartner,
            (await foreign.IngestAsync("haven-edi", "order", OrderPayload("EXT-2"), CancellationToken.None)).Outcome);
        Assert.Null(await foreign.ReplayAsync(ingested.Message!.Id, CancellationToken.None));
    }
}
