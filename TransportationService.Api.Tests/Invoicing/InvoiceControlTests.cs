using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Wave 10: the invoice-control workspace reads the Wave-2 readiness projection, groups READY
/// orders per the customer's InvoiceGrouping preference, queues ReviewRequired orders with
/// their reasons and surfaces approved-but-unapplied incident charges.
/// </summary>
public class InvoiceControlTests
{
    private sealed record Harness(SqliteTestDbContext Db, InvoiceControlService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new InvoiceControlService(db.Context, new DevTenantContext(tenantId)), tenantId);
    }

    private static Customer Customer(Harness h, string name, string grouping)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerNumber = name, Name = name,
            IsActive = true, InvoiceGrouping = grouping,
        };
        h.Db.Context.Customers.Add(customer);
        return customer;
    }

    private static TransportOrder Order(
        Harness h, Customer customer, string number, string readiness, string? reasons = null,
        string? reference = null, decimal? price = 100m)
    {
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = customer.Id,
            OrderNumber = number, OrderDate = new DateOnly(2026, 8, 12),
            Status = TransportOrderStatus.Completed, AgreedPrice = price,
            InvoiceReadiness = readiness, InvoiceReadinessReasons = reasons,
            CustomerReference = reference,
        };
        h.Db.Context.TransportOrders.Add(order);
        return order;
    }

    [Fact]
    public async Task Control_GroupsReadyOrders_PerCustomerPreference_AndQueuesReview()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var perDossier = Customer(h, "Dossierklant", "PerDossier");
        var byReference = Customer(h, "Referentieklant", "ByReference");
        var manual = Customer(h, "Manueel", "Manual");

        var dossier = new TransportDossier
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierNumber = "DOS-0001", CustomerId = perDossier.Id,
        };
        h.Db.Context.TransportDossiers.Add(dossier);
        var dossierOrder = Order(h, perDossier, "ORD-1", "ReadyForInvoice");
        h.Db.Context.DossierOrders.Add(new DossierOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = dossier.Id, TransportOrderId = dossierOrder.Id,
        });
        Order(h, byReference, "ORD-2", "ReadyForInvoice", reference: "PROJECT-A");
        Order(h, byReference, "ORD-3", "ReadyForInvoice", reference: "PROJECT-A", price: 50m);
        Order(h, manual, "ORD-4", "ReadyForInvoice");
        Order(h, manual, "ORD-5", "ReviewRequired", "pricing.stale;pod.missing", price: null);
        await h.Db.Context.SaveChangesAsync();

        var control = await h.Sut.GetAsync(CancellationToken.None);

        var dossierProposal = Assert.Single(control.Proposals, p => p.CustomerName == "Dossierklant");
        Assert.Equal("DOS-0001", dossierProposal.GroupLabel);
        var referenceProposal = Assert.Single(control.Proposals, p => p.CustomerName == "Referentieklant");
        Assert.Equal("Referentie PROJECT-A", referenceProposal.GroupLabel);
        Assert.Equal(150m, referenceProposal.TotalAmount);
        Assert.Equal(2, referenceProposal.Orders.Count);
        var manualProposal = Assert.Single(control.Proposals, p => p.CustomerName == "Manueel");
        Assert.Equal("Klaar voor facturatie", manualProposal.GroupLabel);

        var review = Assert.Single(control.NeedsReview);
        Assert.Equal("ORD-5", review.OrderNumber);
        Assert.Equal(["pricing.stale", "pod.missing"], review.Reasons);
    }

    [Fact]
    public async Task Control_SurfacesApprovedChargesOnLockedOrders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = Customer(h, "Klant", "Manual");
        var order = Order(h, customer, "ORD-9", "NotReady");
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = order.Id,
            Status = OrderPricingStatus.Locked, TariffDate = order.OrderDate, Currency = "EUR",
        });
        h.Db.Context.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Wachturen kraan",
            Description = "x", IncidentType = IncidentType.Delay,
            TransportOrderId = order.Id, CustomerId = customer.Id,
            ResponsibleParty = "Customer", ChargeDecision = "Approved", ChargeAmount = 180m,
        });
        await h.Db.Context.SaveChangesAsync();

        var control = await h.Sut.GetAsync(CancellationToken.None);

        var pending = Assert.Single(control.PendingCharges);
        Assert.Contains("ORD-9", pending);
        Assert.Contains("180", pending);
    }
}
