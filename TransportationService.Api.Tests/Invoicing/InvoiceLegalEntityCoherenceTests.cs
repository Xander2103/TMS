using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Spec Part O: an invoice is issued by exactly one legal entity, and every order on it must
/// belong to that entity. Orders of different entities never combine; a mismatch is a clear
/// validation error, never a silent entity switch. Orders without an entity (pre-entity legacy
/// data) keep invoicing under any entity.
/// </summary>
public class InvoiceLegalEntityCoherenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, InvoiceService Sut, Guid TenantId, Guid CustomerId,
        Guid EntityAId, Guid EntityBId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        var entityA = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Transport BV",
            IsActive = true, IsDefault = true,
        };
        var entityB = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Logistics BV", IsActive = true,
        };
        db.Context.LegalEntities.AddRange(entityA, entityB);
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            IsActive = true, DefaultLegalEntityId = entityA.Id,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new InvoiceService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now), new InvoiceNumberService(db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null))));
        return new Harness(db, sut, tenantId, customerId, entityA.Id, entityB.Id);
    }

    private static async Task<Guid> AddCompletedOrderAsync(Harness h, string orderNumber, Guid? legalEntityId, Guid? tenantId = null, Guid? customerId = null)
    {
        var orderId = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId ?? h.TenantId, CustomerId = customerId ?? h.CustomerId,
            OrderNumber = orderNumber, OrderDate = new(2026, 8, 1), Status = TransportOrderStatus.Completed,
            GoodsDescription = "2 paletten", AgreedPrice = 100m, LegalEntityId = legalEntityId,
        });
        await h.Db.Context.SaveChangesAsync();
        return orderId;
    }

    [Fact]
    public async Task Create_OrdersOfSameEntity_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order1 = await AddCompletedOrderAsync(h, "ORD-1", h.EntityAId);
        var order2 = await AddCompletedOrderAsync(h, "ORD-2", h.EntityAId);

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [order1, order2], [], null, LegalEntityId: h.EntityAId),
            CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Invoice!.Lines.Count);
    }

    [Fact]
    public async Task Create_OrdersOfDifferentEntities_CannotBeCombined()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orderA = await AddCompletedOrderAsync(h, "ORD-A", h.EntityAId);
        var orderB = await AddCompletedOrderAsync(h, "ORD-B", h.EntityBId);

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [orderA, orderB], [], null),
            CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("entiteit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await h.Db.Context.Invoices.CountAsync());
    }

    [Fact]
    public async Task Create_OrderEntityDiffersFromExplicitInvoiceEntity_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orderA = await AddCompletedOrderAsync(h, "ORD-A", h.EntityAId);

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [orderA], [], null, LegalEntityId: h.EntityBId),
            CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("ORD-A", result.Error);
        Assert.Equal(0, await h.Db.Context.Invoices.CountAsync());
    }

    /// <summary>The customer default (A) must never be silently replaced by the order's entity (B).</summary>
    [Fact]
    public async Task Create_OrderEntityDiffersFromResolvedDefault_IsBlocked_NotSilentlyAdopted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orderB = await AddCompletedOrderAsync(h, "ORD-B", h.EntityBId);

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [orderB], [], null),
            CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(0, await h.Db.Context.Invoices.CountAsync());
    }

    /// <summary>Pre-entity legacy orders (LegalEntityId null) stay invoiceable under any entity.</summary>
    [Fact]
    public async Task Create_LegacyOrderWithoutEntity_FollowsInvoiceEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var legacyOrder = await AddCompletedOrderAsync(h, "ORD-L", legalEntityId: null);

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [legacyOrder], [], null, LegalEntityId: h.EntityBId),
            CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        Assert.Equal(h.EntityBId, result.Invoice!.LegalEntityId);
    }

    [Fact]
    public async Task Create_OrderOfOtherTenant_IsRejected_RegardlessOfEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer
        {
            Id = otherCustomerId, TenantId = otherTenantId, CustomerNumber = "KL-9", Name = "Vreemd NV", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var foreignOrder = await AddCompletedOrderAsync(h, "ORD-X", h.EntityAId, tenantId: otherTenantId, customerId: otherCustomerId);

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [foreignOrder], [], null, LegalEntityId: h.EntityAId),
            CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(0, await h.Db.Context.Invoices.Where(i => i.TenantId == h.TenantId).CountAsync());
    }

    /// <summary>
    /// Hard gate at Send for drafts that predate this validation: an order-backed line whose
    /// order belongs to another entity blocks sending.
    /// </summary>
    [Fact]
    public async Task Send_DraftWithMismatchedOrderEntity_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orderA = await AddCompletedOrderAsync(h, "ORD-A", h.EntityAId);
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [orderA], [], null, LegalEntityId: h.EntityAId),
            CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);

        // Simulate a pre-fix inconsistent draft: the order's entity changes after creation.
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderA);
        order.LegalEntityId = h.EntityBId;
        await h.Db.Context.SaveChangesAsync();

        var send = await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, send.Outcome);
        Assert.Contains("ORD-A", send.Error);
    }
}
