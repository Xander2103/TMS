using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Sprint 6 — an order created under a placeholder customer is moved to the real customer once
/// it is known. Operational facts survive; everything the OLD customer decided does not.
/// </summary>
public class OrderCustomerChangeTests
{
    private static readonly DateTime Now = new(2026, 08, 28, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(
        SqliteTestDbContext Db, OrderCustomerChangeService Sut, Guid TenantId,
        Guid PlaceholderId, Guid RealCustomerId, Guid OrderId, Guid EntityA, Guid EntityB);

    private static async Task<Harness> SeedAsync(bool realCustomerHasTariff = true, string? realCustomerLanguage = "fr")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var placeholderId = Guid.NewGuid();
        var realCustomerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });
        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = entityA, TenantId = tenantId, LegalName = "Entiteit A", IsActive = true, IsDefault = true },
            new LegalEntity { Id = entityB, TenantId = tenantId, LegalName = "Entiteit B", IsActive = true });

        db.Context.Customers.AddRange(
            new Customer
            {
                Id = placeholderId, TenantId = tenantId, CustomerNumber = "TMP-1", Name = "VCB tijdelijk",
                IsActive = true, DefaultLegalEntityId = entityA, VatTreatment = VatTreatment.DomesticVat,
            },
            new Customer
            {
                Id = realCustomerId, TenantId = tenantId, CustomerNumber = "KL-9", Name = "Client SA",
                IsActive = true, DefaultLegalEntityId = entityB, VatTreatment = VatTreatment.ReverseCharge,
                InvoiceLanguageCode = realCustomerLanguage,
            });

        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = placeholderId, OrderNumber = "ORD-1",
            OrderDate = new DateOnly(2026, 8, 10), Status = TransportOrderStatus.Completed,
            AgreedPrice = 500m, LegalEntityId = entityA,
        });

        if (realCustomerHasTariff)
        {
            db.Context.PriceRules.Add(new PriceRule
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = realCustomerId,
                Name = "Distributie", Basis = PriceRuleBasis.PerKm, UnitPrice = 2m,
                EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, Currency = "EUR",
            });
        }

        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        return new Harness(db, new OrderCustomerChangeService(db.Context, tenant, audit),
            tenantId, placeholderId, realCustomerId, orderId, entityA, entityB);
    }

    private static async Task AddOperationalDataAsync(Harness h)
    {
        // Real parents: the POD references both a trip and the stop it was signed at.
        var stopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-1", TripDate = new DateOnly(2026, 8, 10),
        });
        h.Db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = stopId, TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1,
            StopType = StopType.Unloading, LocationName = "Magazijn Noord", Address = "Noorderlaan 10",
            City = "Antwerpen", PostalCode = "2030", CountryCode = "BE", SnapshotAt = Now,
        });
        h.Db.Context.CargoItems.Add(new CargoItem
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            Description = "20 paletten", ExpectedQuantity = 20m, Sequence = 1,
        });
        await h.Db.Context.SaveChangesAsync();

        h.Db.Context.ScanEvents.Add(new ScanEvent
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            Barcode = "BC-1", OccurredAt = Now, ScanType = ScanType.Load,
        });
        h.Db.Context.ProofsOfDelivery.Add(new ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            TripId = tripId, TransportOrderStopId = stopId,
            RecipientName = "Ontvanger", Outcome = PodOutcome.Complete,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    private static async Task AddPricingAsync(Harness h)
    {
        h.Db.Context.TransportOrderPricingLines.AddRange(
            new TransportOrderPricingLine
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 0,
                Label = "Transport", Amount = 400m, Source = "Regel", RuleName = "Tarief A",
                AgreementName = "Kaart A", Kind = OrderPriceLineKind.Auto,
            },
            new TransportOrderPricingLine
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 1,
                Label = "Wachttijd", Amount = 60m, Source = "Manueel", Kind = OrderPriceLineKind.Manual,
            },
            new TransportOrderPricingLine
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId, Sequence = 2,
                Label = "Extra stop", Amount = 40m, Source = "Regel", RuleName = "Tarief A",
                AgreementName = "Kaart A", Kind = OrderPriceLineKind.AutoAdjusted,
            });
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            TariffDate = new DateOnly(2026, 8, 10), Currency = "EUR",
            CalculatedTotal = 500m, AgreementNames = "Kaart A", Status = OrderPricingStatus.Locked,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    private static ChangeOrderCustomerRequest Request(Guid customerId) => new(customerId, "Echte klant bekend");

    // ---------------------------------------------------------- scenario A/C

    [Fact]
    public async Task A_MovingToTheRealCustomer_KeepsEveryOperationalFact()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddOperationalDataAsync(h);
        await AddPricingAsync(h);

        var result = await h.Sut.ApplyAsync(h.OrderId, Request(h.RealCustomerId), CancellationToken.None);

        Assert.NotNull(result);
        var order = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == h.OrderId);
        Assert.Equal(h.RealCustomerId, order.CustomerId);

        // Stops, goods, scans and POD are facts about what happened — untouched.
        var stop = await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.TransportOrderId == h.OrderId);
        Assert.Equal("Noorderlaan 10", stop.Address);
        Assert.Equal("Magazijn Noord", stop.LocationName);
        Assert.Single(await h.Db.Context.CargoItems.AsNoTracking().Where(c => c.TransportOrderId == h.OrderId).ToListAsync());
        Assert.Single(await h.Db.Context.ScanEvents.AsNoTracking().Where(s => s.TransportOrderId == h.OrderId).ToListAsync());
        Assert.Single(await h.Db.Context.ProofsOfDelivery.AsNoTracking().Where(p => p.TransportOrderId == h.OrderId).ToListAsync());
    }

    // ---------------------------------------------------------- scenario B

    [Fact]
    public async Task B_AutomaticPricingFromTheOldCustomer_IsNotSilentlyRetained()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddPricingAsync(h);

        await h.Sut.ApplyAsync(h.OrderId, Request(h.RealCustomerId), CancellationToken.None);

        var lines = await h.Db.Context.TransportOrderPricingLines.AsNoTracking()
            .Where(l => l.TransportOrderId == h.OrderId).ToListAsync();

        // The automatic line from customer A is gone.
        Assert.DoesNotContain(lines, l => l.Kind == OrderPriceLineKind.Auto);
        Assert.DoesNotContain(lines, l => l.Label == "Transport");

        // The manual line survives, and is still plainly manual.
        var manual = Assert.Single(lines, l => l.Label == "Wachttijd");
        Assert.Equal(OrderPriceLineKind.Manual, manual.Kind);
        Assert.Equal(60m, manual.Amount);

        // The adjusted line's amount was DERIVED from the OLD customer's tariff, so it is not
        // allowed to count for the new customer by itself: it survives as an unconfirmed
        // proposal (excluded from the total until explicitly confirmed) that names where it
        // came from — never a clean, silently valid price for the new customer.
        var adjusted = Assert.Single(lines, l => l.Label == "Extra stop");
        Assert.Equal(OrderPriceLineKind.Proposed, adjusted.Kind);
        Assert.True(adjusted.Proposed);
        Assert.Equal(40m, adjusted.Amount);
        Assert.Null(adjusted.RuleName);
        Assert.Null(adjusted.AgreementName);
        Assert.Null(adjusted.OriginalAmount);
        Assert.Contains("VCB tijdelijk", adjusted.AdjustReason);
        Assert.Contains("bevestigen", adjusted.AdjustReason);

        // The order needs a price decision again, and the frozen numbers are flagged stale so
        // invoice readiness cannot report the order as ready on the old customer's figures.
        var order = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == h.OrderId);
        Assert.Null(order.AgreedPrice);
        Assert.NotEqual(InvoiceReadinessEvaluator.ReadyForInvoice, order.InvoiceReadiness);
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots.AsNoTracking()
            .FirstAsync(s => s.TransportOrderId == h.OrderId);
        Assert.Equal(OrderPricingStatus.Draft, snapshot.Status);
        Assert.True(snapshot.IsStale);
        Assert.Null(snapshot.CalculatedTotal);
        // Only the genuinely manual line still counts; the proposal does not.
        Assert.Equal(60m, snapshot.LinesTotal);
    }

    [Fact]
    public async Task WithoutATariffForTheNewCustomer_TheOrderIsFlaggedForPricingReview()
    {
        var h = await SeedAsync(realCustomerHasTariff: false);
        using var _ = h.Db;
        await AddPricingAsync(h);

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);

        Assert.True(impact!.NeedsPricingReview);
    }

    [Fact]
    public async Task WithATariffForTheNewCustomer_NoReviewFlagIsRaised()
    {
        var h = await SeedAsync(realCustomerHasTariff: true);
        using var _ = h.Db;

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);

        Assert.False(impact!.NeedsPricingReview);
    }

    [Fact]
    public async Task CarriedOverAdjustedAmounts_AlwaysRequireReview_EvenWithATariff()
    {
        var h = await SeedAsync(realCustomerHasTariff: true);
        using var _ = h.Db;
        await AddPricingAsync(h);

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);

        Assert.Equal(1, impact!.AdjustedLinesFlaggedForReview);
        Assert.True(impact.NeedsPricingReview);
    }

    [Fact]
    public async Task WithoutADefaultEntityForTheNewCustomer_TheTenantDefaultApplies_NeverNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var target = await h.Db.Context.Customers.FirstAsync(c => c.Id == h.RealCustomerId);
        target.DefaultLegalEntityId = null;
        // …and the old entity is not allowed for the new customer.
        h.Db.Context.Set<CustomerAllowedLegalEntity>().Add(new CustomerAllowedLegalEntity
        { Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.RealCustomerId, LegalEntityId = h.EntityB });
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);

        Assert.Equal(h.EntityA, impact!.NewLegalEntityId); // tenant default (IsDefault = true)
    }

    // ------------------------------------------------------- scenarios D & H

    [Fact]
    public async Task D_TheInvoicingEntityFollowsTheNewCustomersDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var impact = await h.Sut.ApplyAsync(h.OrderId, Request(h.RealCustomerId), CancellationToken.None);

        Assert.True(impact!.LegalEntityChanges);
        var order = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == h.OrderId);
        Assert.Equal(h.EntityB, order.LegalEntityId);
    }

    [Fact]
    public async Task H_ThePreviewShowsTheNewCommercialDefaults()
    {
        var h = await SeedAsync(realCustomerLanguage: "fr");
        using var _ = h.Db;

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);

        Assert.Equal("fr", impact!.NewInvoiceLanguage);
        Assert.Equal("ReverseCharge", impact.NewVatTreatment);
        Assert.Equal("VCB tijdelijk", impact.CurrentCustomerName);
        Assert.Equal("Client SA", impact.NewCustomerName);
    }

    // ----------------------------------------------------------- scenario G

    [Fact]
    public async Task G_AnOrderOnASentInvoice_CannotBeMoved()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.PlaceholderId, InvoiceNumber = "FAC-1",
            InvoiceDate = new DateOnly(2026, 8, 20), DueDate = new DateOnly(2026, 9, 20),
            Status = InvoiceStatus.Sent, LegalEntityId = h.EntityA,
        });
        h.Db.Context.Set<InvoiceLine>().Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = h.OrderId,
            Sequence = 0, Description = "Transport", Quantity = 1m, UnitPrice = 500m, VatRatePercent = 21m,
        });
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);
        Assert.NotNull(impact!.BlockedReason);
        Assert.Contains("creditnota", impact.BlockedReason!);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ApplyAsync(h.OrderId, Request(h.RealCustomerId), CancellationToken.None));

        // The order still belongs to the invoiced customer.
        var order = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == h.OrderId);
        Assert.Equal(h.PlaceholderId, order.CustomerId);
    }

    [Fact]
    public async Task F_AnOrderOnADraftInvoice_MayStillBeMoved()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.PlaceholderId, InvoiceNumber = "CONCEPT",
            InvoiceDate = new DateOnly(2026, 8, 20), DueDate = new DateOnly(2026, 9, 20),
            Status = InvoiceStatus.Draft, LegalEntityId = h.EntityA,
        });
        h.Db.Context.Set<InvoiceLine>().Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = h.OrderId,
            Sequence = 0, Description = "Transport", Quantity = 1m, UnitPrice = 500m, VatRatePercent = 21m,
        });
        // Real state: an order on a concept invoice already carries Status = Invoiced.
        var invoicedOrder = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == h.OrderId);
        invoicedOrder.Status = TransportOrderStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.RealCustomerId, CancellationToken.None);
        Assert.Null(impact!.BlockedReason);
        // The user is told the concept invoice will be released.
        Assert.Equal(1, impact.DraftInvoiceLinesReleased);

        await h.Sut.ApplyAsync(h.OrderId, Request(h.RealCustomerId), CancellationToken.None);

        // Sprint 6E: the concept invoice no longer holds an order of another customer.
        Assert.Empty(await h.Db.Context.InvoiceLines.AsNoTracking()
            .Where(l => l.TransportOrderId == h.OrderId).ToListAsync());
        // The invoice itself is kept so the invoicing user can rebuild the proposal.
        Assert.True(await h.Db.Context.Invoices.AsNoTracking().AnyAsync(i => i.Id == invoiceId));
        // Audit fix: the released order is invoiceable again under the new customer.
        var released = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == h.OrderId);
        Assert.Equal(TransportOrderStatus.Completed, released.Status);
    }

    // -------------------------------------------------------------- guards

    [Fact]
    public async Task AReasonIsRequired()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ApplyAsync(h.OrderId, new ChangeOrderCustomerRequest(h.RealCustomerId, "  "), CancellationToken.None));
    }

    [Fact]
    public async Task TheChangeIsAudited_WithBothCustomersAndTheReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddPricingAsync(h);

        await h.Sut.ApplyAsync(h.OrderId, Request(h.RealCustomerId), CancellationToken.None);

        var entry = await h.Db.Context.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.EntityId == h.OrderId.ToString() && a.Action == "CustomerChanged");
        Assert.Contains(h.PlaceholderId.ToString(), entry.OldValuesJson);
        Assert.Contains(h.RealCustomerId.ToString(), entry.NewValuesJson);
        Assert.Contains("Echte klant bekend", entry.NewValuesJson);
    }

    [Fact]
    public async Task ACustomerFromAnotherTenant_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreign = new Customer
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CustomerNumber = "X-1", Name = "Vreemde", IsActive = true,
        };
        h.Db.Context.Customers.Add(foreign);
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ApplyAsync(h.OrderId, Request(foreign.Id), CancellationToken.None));
    }

    [Fact]
    public async Task MovingToTheSameCustomer_IsRefusedAsANoOp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var impact = await h.Sut.PreviewAsync(h.OrderId, h.PlaceholderId, CancellationToken.None);
        Assert.NotNull(impact!.BlockedReason);
    }
}
