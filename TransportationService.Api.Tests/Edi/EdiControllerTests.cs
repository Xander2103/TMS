using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Edi.Controllers;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Edi.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Edi;

/// <summary>
/// Phase 10 (corrections wave 4, EDI redesign): partner admin API, mapping deletion, dashboard
/// stats, the dry-run validate endpoint, the mappingIssues filter, and the read/test/retry vs.
/// manage permission split on every action. Direct controller instantiation, same convention as
/// <c>NotificationRulesControllerTests</c> — no HTTP context is touched by any of these actions.
/// </summary>
public class EdiControllerTests
{
    private static readonly DateTime Now = new(2026, 07, 30, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(
        SqliteTestDbContext Db, EdiController Sut, Guid TenantId, Guid PartnerId, Guid CustomerId, Guid LocationId, Guid MappingId);

    private static T UnwrapValue<T>(ActionResult<T> result) =>
        result.Value ?? (T)((ObjectResult)result.Result!).Value!;

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partnerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });
        db.Context.TenantSettings.Add(new TransportationService.Api.Modules.Tenancy.Entities.TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
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
            Id = mappingId, TenantId = tenantId, TradingPartnerId = partnerId,
            ExternalLocationCode = "TERMINAL-77", LocationId = locationId,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var orders = new TransportOrderService(db.Context, tenant, audit, clock);
        var service = new EdiService(db.Context, tenant, audit, orders, clock);
        var sut = new EdiController(service, db.Context, tenant, audit, clock);
        return new Harness(db, sut, tenantId, partnerId, customerId, locationId, mappingId);
    }

    private static string OrderPayload(string externalId, string locationCode) => System.Text.Json.JsonSerializer.Serialize(new
    {
        externalOrderId = externalId,
        customerReference = "PO-9",
        goodsDescription = "20 paletten via EDI",
        stops = new object[]
        {
            new { type = "Loading", externalLocationCode = locationCode },
            new { type = "Unloading", city = "Gent" },
        },
        cargoItems = new object[] { new { description = "Pallet", quantity = 20 } },
    });

    // --- Partner update ---

    [Fact]
    public async Task UpdatePartner_PersistsChangesAndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = new EdiController.UpdatePartnerRequest(
            "Haven EDI (bijgewerkt)", h.CustomerId, "EXT-123", "generic-json-v1", false, "interne notitie");
        await h.Sut.UpdatePartner(h.PartnerId, request, CancellationToken.None);

        var partner = await h.Db.Context.TradingPartners.SingleAsync(p => p.Id == h.PartnerId);
        Assert.Equal("Haven EDI (bijgewerkt)", partner.Name);
        Assert.Equal("EXT-123", partner.ExternalCustomerIdentifier);
        Assert.False(partner.IsActive);
        Assert.Equal("interne notitie", partner.Notes);

        var log = Assert.Single(h.Db.Context.AuditLogs.Where(a => a.EntityType == "TradingPartner" && a.Action == "Updated"));
        Assert.NotNull(log.OldValuesJson);
        Assert.NotNull(log.NewValuesJson);
    }

    [Fact]
    public async Task UpdatePartner_UnknownId_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.UpdatePartner(Guid.NewGuid(),
            new EdiController.UpdatePartnerRequest("X", null, null, "generic-json-v1", true, null),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdatePartner_CrossTenantCustomer_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignCustomerId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now });
        h.Db.Context.Customers.Add(new Customer
        {
            Id = foreignCustomerId, TenantId = Guid.NewGuid(), CustomerNumber = "KL-X", Name = "Vreemde klant", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var request = new EdiController.UpdatePartnerRequest(
            "Haven EDI", foreignCustomerId, null, "generic-json-v1", true, null);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpdatePartner(h.PartnerId, request, CancellationToken.None));
        Assert.Contains("customerId", exception.FieldErrors!.Keys);

        var unchanged = await h.Db.Context.TradingPartners.SingleAsync(p => p.Id == h.PartnerId);
        Assert.Equal(h.CustomerId, unchanged.CustomerId);
    }

    [Fact]
    public async Task UpdatePartner_BlankName_ThrowsValidationException()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.UpdatePartner(h.PartnerId,
            new EdiController.UpdatePartnerRequest("   ", null, null, "generic-json-v1", true, null),
            CancellationToken.None));
    }

    // --- Location mapping delete ---

    [Fact]
    public async Task DeleteLocationMapping_RemovesRowAndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.DeleteLocationMapping(h.PartnerId, h.MappingId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await h.Db.Context.EdiPartnerLocations.AnyAsync(l => l.Id == h.MappingId));
        var log = Assert.Single(h.Db.Context.AuditLogs.Where(a => a.EntityType == "EdiPartnerLocation" && a.Action == "Deleted"));
        Assert.NotNull(log.OldValuesJson);
    }

    [Fact]
    public async Task DeleteLocationMapping_UnknownMapping_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.DeleteLocationMapping(h.PartnerId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // --- Tenant isolation on the new endpoints ---

    [Fact]
    public async Task UpdatePartner_AndDeleteLocationMapping_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreignAudit = new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null));
        var foreignClock = new TestClock(Now);
        var foreignOrders = new TransportOrderService(h.Db.Context, foreignTenant, foreignAudit, foreignClock);
        var foreignService = new EdiService(h.Db.Context, foreignTenant, foreignAudit, foreignOrders, foreignClock);
        var foreignSut = new EdiController(foreignService, h.Db.Context, foreignTenant, foreignAudit, foreignClock);

        var updateResult = await foreignSut.UpdatePartner(h.PartnerId,
            new EdiController.UpdatePartnerRequest("Gekaapt", null, null, "generic-json-v1", true, null),
            CancellationToken.None);
        Assert.IsType<NotFoundResult>(updateResult);

        var deleteResult = await foreignSut.DeleteLocationMapping(h.PartnerId, h.MappingId, CancellationToken.None);
        Assert.IsType<NotFoundResult>(deleteResult);

        // Untouched: neither cross-tenant call mutated the original tenant's data.
        var partner = await h.Db.Context.TradingPartners.SingleAsync(p => p.Id == h.PartnerId);
        Assert.Equal("Haven EDI", partner.Name);
        Assert.True(await h.Db.Context.EdiPartnerLocations.AnyAsync(l => l.Id == h.MappingId));
    }

    // --- Stats ---

    [Fact]
    public async Task Stats_CountsMatchSeededScenario()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // One processed message (via real ingest — resolves through the seeded mapping).
        await h.Sut.Ingest("haven-edi", "order", new EdiController.IngestRequest(OrderPayload("EXT-A", "TERMINAL-77")), CancellationToken.None);
        // One mapping failure (unknown location code).
        await h.Sut.Ingest("haven-edi", "order", new EdiController.IngestRequest(OrderPayload("EXT-B", "ONBEKEND")), CancellationToken.None);
        // A second partner with no linked customer.
        h.Db.Context.TradingPartners.Add(new TradingPartner
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "no-customer", Name = "Zonder klant",
            CustomerId = null, MappingProfile = "generic-json-v1", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var stats = UnwrapValue(await h.Sut.Stats(CancellationToken.None));

        Assert.Equal(1, stats.PerStatus.GetValueOrDefault("Processed"));
        Assert.Equal(1, stats.PerStatus.GetValueOrDefault("Failed"));
        Assert.Equal(1, stats.Failed);
        Assert.Equal(0, stats.DeadLettered);
        Assert.Equal(1, stats.MappingIssues);
        Assert.Equal(1, stats.ProcessedLast7Days);
        Assert.Equal(2, stats.TotalPartners);
        Assert.Equal(1, stats.PartnersWithoutCustomer);
    }

    // --- Validate (dry run) ---

    [Fact]
    public async Task Validate_ValidPayload_ReturnsWouldCreateSummary_AndPersistsNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = (OkObjectResult)await h.Sut.Validate(
            new EdiController.ValidateRequest("haven-edi", "order", OrderPayload("EXT-DRY", "TERMINAL-77")),
            CancellationToken.None);
        var payload = result.Value!;
        var valid = (bool)payload.GetType().GetProperty("Valid")!.GetValue(payload)!;
        var wouldCreate = payload.GetType().GetProperty("WouldCreate")!.GetValue(payload);

        Assert.True(valid);
        Assert.NotNull(wouldCreate);
        Assert.Empty(h.Db.Context.EdiMessages);
        Assert.Empty(h.Db.Context.TransportOrders);
    }

    [Fact]
    public async Task Validate_InvalidPayload_ReturnsDutchErrors_AndPersistsNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var missingStops = System.Text.Json.JsonSerializer.Serialize(new { externalOrderId = "EXT-X", goodsDescription = "leeg" });

        var result = (OkObjectResult)await h.Sut.Validate(
            new EdiController.ValidateRequest("haven-edi", "order", missingStops), CancellationToken.None);
        var payload = result.Value!;
        var valid = (bool)payload.GetType().GetProperty("Valid")!.GetValue(payload)!;
        var errors = (IReadOnlyList<string>)payload.GetType().GetProperty("Errors")!.GetValue(payload)!;

        Assert.False(valid);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("stop", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(h.Db.Context.EdiMessages);
        Assert.Empty(h.Db.Context.TransportOrders);
    }

    [Fact]
    public async Task Validate_UnknownLocationCode_ReturnsMappingError_AndPersistsNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = (OkObjectResult)await h.Sut.Validate(
            new EdiController.ValidateRequest("haven-edi", "order", OrderPayload("EXT-Y", "ONBEKEND")), CancellationToken.None);
        var payload = result.Value!;
        var valid = (bool)payload.GetType().GetProperty("Valid")!.GetValue(payload)!;
        var errors = (IReadOnlyList<string>)payload.GetType().GetProperty("Errors")!.GetValue(payload)!;

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains("locatiecode"));
        Assert.Empty(h.Db.Context.EdiMessages);
    }

    // --- mappingIssues filter ---

    [Fact]
    public async Task Messages_MappingIssuesFilter_MatchesNewFailureKindAndLegacyText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // New-style: FailureKind set by the current Fail() implementation.
        await h.Sut.Ingest("haven-edi", "order", new EdiController.IngestRequest(OrderPayload("EXT-MAP", "ONBEKEND")), CancellationToken.None);
        // A validation failure — NOT a mapping issue.
        var badPayload = System.Text.Json.JsonSerializer.Serialize(new { externalOrderId = "EXT-BAD", goodsDescription = "leeg" });
        await h.Sut.Ingest("haven-edi", "order", new EdiController.IngestRequest(badPayload), CancellationToken.None);
        // Legacy row: pre-migration data with FailureKind == null but the old mapping error text.
        h.Db.Context.EdiMessages.Add(new EdiMessage
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Direction = EdiDirection.Inbound, TradingPartnerId = h.PartnerId,
            MessageType = "order", ExternalReference = "EXT-LEGACY", PayloadJson = "{}", PayloadHash = "legacy-hash",
            Status = EdiProcessingStatus.Failed, AttemptCount = 1,
            ErrorDetail = "De handelspartner is niet aan een klant gekoppeld.", FailureKind = null,
        });
        await h.Db.Context.SaveChangesAsync();

        var page = UnwrapValue(await h.Sut.Messages(null, null, null, true, null, null, null, CancellationToken.None));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, i => Assert.True(i.MappingIssue));
        Assert.Contains(page.Items, i => i.ExternalReference == "EXT-MAP");
        Assert.Contains(page.Items, i => i.ExternalReference == "EXT-LEGACY");
        Assert.DoesNotContain(page.Items, i => i.ExternalReference == "EXT-BAD");
    }

    [Fact]
    public async Task Messages_SearchAndPartnerFilters_Apply()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.Ingest("haven-edi", "order", new EdiController.IngestRequest(OrderPayload("EXT-FIND-ME", "TERMINAL-77")), CancellationToken.None);
        await h.Sut.Ingest("haven-edi", "order", new EdiController.IngestRequest(OrderPayload("EXT-OTHER", "TERMINAL-77")), CancellationToken.None);

        var bySearch = UnwrapValue(await h.Sut.Messages(null, null, null, null, "FIND-ME", null, null, CancellationToken.None));
        Assert.Single(bySearch.Items);
        Assert.Equal("EXT-FIND-ME", bySearch.Items[0].ExternalReference);

        var byPartner = UnwrapValue(await h.Sut.Messages(null, null, h.PartnerId, null, null, null, null, CancellationToken.None));
        Assert.Equal(2, byPartner.TotalCount);

        var byUnknownPartner = UnwrapValue(await h.Sut.Messages(null, null, Guid.NewGuid(), null, null, null, null, CancellationToken.None));
        Assert.Empty(byUnknownPartner.Items);
    }

    // --- Permission split (any-of codes per action, per the design decision table) ---

    public static IEnumerable<object[]> ExpectedPermissionsByAction() =>
        new List<object[]>
        {
            new object[] { nameof(EdiController.Ingest), new[] { PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.Messages), new[] { PermissionCodes.EdiView, PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.MessageDetail), new[] { PermissionCodes.EdiView, PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.Replay), new[] { PermissionCodes.EdiRetry, PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.Partners), new[] { PermissionCodes.EdiView, PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.UpsertPartner), new[] { PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.UpdatePartner), new[] { PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.AddLocationMapping), new[] { PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.DeleteLocationMapping), new[] { PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.Stats), new[] { PermissionCodes.EdiView, PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.Validate), new[] { PermissionCodes.EdiTest, PermissionCodes.EdiManage } },
            new object[] { nameof(EdiController.Simulate), new[] { PermissionCodes.EdiTest, PermissionCodes.EdiManage } },
        };

    [Theory]
    [MemberData(nameof(ExpectedPermissionsByAction))]
    public void Action_RequiresExpectedPermissions(string methodName, string[] expectedCodes)
    {
        var method = typeof(EdiController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == methodName && m.GetCustomAttribute<RequirePermissionAttribute>() != null);
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>()!;

        Assert.Equal(expectedCodes, attribute.PermissionCodes);
    }

    [Fact]
    public void EdiView_Alone_CannotReplayOrManage()
    {
        // edi.view is deliberately absent from Replay/Manage-only actions' any-of list — a
        // view-only caller's RequirePermission check fails at the filter level (verified via the
        // codes above), never reaching these actions' bodies.
        var replay = typeof(EdiController).GetMethod(nameof(EdiController.Replay))!;
        var codes = replay.GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;
        Assert.DoesNotContain(PermissionCodes.EdiView, codes);

        var updatePartner = typeof(EdiController).GetMethod(nameof(EdiController.UpdatePartner))!;
        var manageCodes = updatePartner.GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;
        Assert.DoesNotContain(PermissionCodes.EdiView, manageCodes);
        Assert.DoesNotContain(PermissionCodes.EdiTest, manageCodes);
        Assert.DoesNotContain(PermissionCodes.EdiRetry, manageCodes);
    }
}
