using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

/// <summary>
/// Wave 2 §2: the allowed-entities policy on the ORDER and INVOICE side. An explicit entity
/// outside the customer's set is a validation error naming the allowed entities; changing an
/// order to a non-default entity needs dossiers.override_entity; the inherited default keeps
/// working without any right.
/// </summary>
public class OrderAndInvoiceEntityGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, InvoiceService Invoices, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid EntityAId, Guid EntityBId, Guid EntityCId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var entityC = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
            InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = entityA, TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true },
            new LegalEntity { Id = entityB, TenantId = tenantId, LegalName = "Acme Logistics BV", IsActive = true },
            new LegalEntity { Id = entityC, TenantId = tenantId, LegalName = "Acme Kranen BV", IsActive = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV",
            IsActive = true, DefaultLegalEntityId = entityA,
        });
        // Restriction: only A and B.
        db.Context.CustomerAllowedLegalEntities.AddRange(
            new CustomerAllowedLegalEntity { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, LegalEntityId = entityA },
            new CustomerAllowedLegalEntity { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, LegalEntityId = entityB });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var permissions = new PermissionSet();
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now),
            new PricingEngine(db.Context, tenant), currentUser, permissions);
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            new Modules.Accounting.Services.AccountingService(db.Context, tenant, audit));
        return new Harness(db, orders, invoices, permissions, tenantId, customerId, entityA, entityB, entityC);
    }

    private static TransportOrderStopInput Stop(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, Guid? legalEntityId = null) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Pallets", 5, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")],
        LegalEntityId: legalEntityId);

    [Fact]
    public async Task OrderCreate_InheritsTheDefault_AndRejectsAnEntityOutsideTheSet()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var inherited = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, inherited.Outcome);
        Assert.Equal(h.EntityAId, inherited.Order!.LegalEntityId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Orders.CreateAsync(Request(h.CustomerId, h.EntityCId), CancellationToken.None));
        Assert.Contains("Acme Transport BV", ex.Message);
        Assert.Contains("Acme Logistics BV", ex.Message);
    }

    /// <summary>
    /// INVARIANT CHANGED (wave 1 blocker C-02): the plain header update no longer moves an order
    /// to another invoicing entity at all — it refuses, whatever rights the caller holds. The
    /// override right, the mandatory reason, the sent-invoice guard and the draft-line release
    /// live in the dedicated flow (ChangeLegalEntityAsync), which this test now exercises for
    /// both halves of the original assertion.
    /// </summary>
    [Fact]
    public async Task OrderUpdate_ToNonDefaultEntity_RequiresTheOverrideRight()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var order = created.Order!;

        var update = new UpdateTransportOrderRequest(
            order.CustomerId, order.CustomerReference, order.OrderDate, order.GoodsDescription, order.Quantity,
            order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount, order.AdrRequired, order.CraneRequired,
            order.AgreedPrice, order.Notes,
            order.Stops.Select(s => new TransportOrderStopInput(
                    s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
                .ToList(),
            LegalEntityId: h.EntityBId);

        // The header edit refuses the move outright and points at the dedicated flow.
        var refused = await h.Orders.UpdateAsync(order.Id, update, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains("Entiteit wijzigen", refused.Error!);
        Assert.Equal(h.EntityAId, (await h.Orders.GetByIdAsync(order.Id, CancellationToken.None))!.LegalEntityId);

        // The dedicated flow still enforces the override right...
        var change = new ChangeOrderLegalEntityRequest(h.EntityBId, "Klant factureert via BV B");
        var denied = await h.Orders.ChangeLegalEntityAsync(order.Id, change, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, denied.Outcome);
        Assert.Contains("geen rechten", denied.Error!);

        // ...and applies the move once it is held.
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var allowed = await h.Orders.ChangeLegalEntityAsync(order.Id, change, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, allowed.Outcome);
        Assert.Equal(h.EntityBId, allowed.Order!.LegalEntityId);
    }

    [Fact]
    public async Task InvoiceCreate_RejectsAnEntityOutsideTheCustomerSet()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [],
            [new ManualInvoiceLineInput("Los", 1m, 100m, 21m)], null,
            LegalEntityId: h.EntityCId), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("niet toegestaan", result.Error!);
    }
}
