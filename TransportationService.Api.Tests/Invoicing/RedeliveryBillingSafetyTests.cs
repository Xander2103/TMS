using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// R-N1 + R-N2: a failed delivery and its redelivery are one transport, billed once. The original
/// carries the price, the redelivery needs an explicit decision, and an extra charge goes through
/// the approved incident charge. "Gratis" after execution exists only inside such a pair.
/// </summary>
public class RedeliveryBillingSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 30);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = [PermissionCodes.OrdersOverridePrice, PermissionCodes.ProblemsApproveCharge];

        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed class Harness : IDisposable
    {
        public required SqliteTestDbContext Db { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid CustomerId { get; init; }
        public required InvoiceService Invoices { get; init; }
        public required InvoiceControlService Control { get; init; }
        public required TransportOrderService Orders { get; init; }
        public required IncidentService Incidents { get; init; }
        public required PermissionSet Permissions { get; init; }

        public TransportOrder Order(string number, decimal? price, Guid? redeliveryOf = null, TransportOrderStatus status = TransportOrderStatus.Completed)
        {
            var order = new TransportOrder
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CustomerId = CustomerId, OrderNumber = number,
                OrderDate = Day, Status = status, GoodsDescription = "20 paletten", AgreedPrice = price,
                RedeliveryOfOrderId = redeliveryOf,
                InvoiceReadiness = status == TransportOrderStatus.Completed
                    ? (price > 0m ? InvoiceReadinessEvaluator.ReadyForInvoice : InvoiceReadinessEvaluator.ReviewRequired)
                    : InvoiceReadinessEvaluator.NotReady,
                InvoiceReadinessReasons = status == TransportOrderStatus.Completed && !(price > 0m) ? "pricing.none" : null,
            };
            Db.Context.TransportOrders.Add(order);
            Db.Context.SaveChanges();
            return order;
        }

        public (TransportOrder Original, TransportOrder Redelivery) Pair(decimal? originalPrice = 300m, decimal? redeliveryPrice = 300m)
        {
            var original = Order("ORD-0002", originalPrice);
            return (original, Order("ORD-0003", redeliveryPrice, original.Id));
        }

        public async Task<InvoiceDetailDto> InvoiceAsync(params Guid[] orderIds)
        {
            var result = await Invoices.CreateAsync(
                new CreateInvoiceRequest(CustomerId, null, orderIds, [], null), CancellationToken.None);
            Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
            return result.Invoice!;
        }

        public async Task<UninvoicedOrderDto> CandidateAsync(Guid id) =>
            (await Invoices.ListUninvoicedOrdersAsync(CustomerId, CancellationToken.None)).Single(o => o.Id == id);

        public async Task ReloadAsync(params TransportOrder[] orders)
        {
            foreach (var order in orders)
            {
                await Db.Context.Entry(order).ReloadAsync();
            }
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", VatNumber = "BE0123456789", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var permissions = new PermissionSet();
        return new Harness
        {
            Db = db, TenantId = tenantId, CustomerId = customerId, Permissions = permissions,
            Invoices = new InvoiceService(db.Context, tenant, audit, clock, new InvoiceNumberService(db.Context, tenant),
                new CustomerBillingConfigService(db.Context, tenant, audit, clock), new AccountingService(db.Context, tenant, audit)),
            Control = new InvoiceControlService(db.Context, tenant),
            Orders = new TransportOrderService(db.Context, tenant, audit, clock, null, user, permissions),
            Incidents = new IncidentService(db.Context, tenant, audit, new NotificationService(db.Context, tenant, user, clock), clock, permissions, user),
        };
    }

    // ------------------------------------------------------------ R-N2: billing candidates

    [Fact]
    public async Task Builder_OffersTheOriginal_AndHoldsTheRedelivery_WithAReason()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair();

        var charged = await h.CandidateAsync(original.Id);
        var held = await h.CandidateAsync(redelivery.Id);

        Assert.True(charged.IsInvoiceable);
        Assert.Null(charged.BillingHoldOrderNumber);
        Assert.False(held.IsInvoiceable);
        Assert.Equal(InvoiceReadinessEvaluator.ReviewRequired, held.InvoiceReadiness);
        Assert.Contains(RedeliveryBillingRule.OriginalCharged, held.InvoiceReadinessReasons);
        Assert.Equal("ORD-0002", held.BillingHoldOrderNumber);
    }

    [Fact]
    public async Task Create_RefusesTheRedelivery_EvenWhenAskedForExplicitly_AndBillsTheOriginalOnce()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair();

        var refused = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [original.Id, redelivery.Id], [], null), CancellationToken.None);
        Assert.NotEqual(InvoiceOperationOutcome.Success, refused.Outcome);
        Assert.Contains("ORD-0002", refused.Error);

        var invoice = await h.InvoiceAsync(original.Id);
        Assert.Equal(300m, invoice.Lines.Sum(l => l.Quantity * l.UnitPrice));
        Assert.DoesNotContain(invoice.Lines, l => l.Description.Contains("ORD-0003"));
    }

    [Fact]
    public async Task Control_ProposesOnlyTheOriginal_AndQueuesTheRedeliveryForReview()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair();

        var control = await h.Control.GetAsync(CancellationToken.None);

        var proposal = Assert.Single(control.Proposals);
        Assert.Equal(original.Id, Assert.Single(proposal.Orders).TransportOrderId);
        Assert.Equal(300m, proposal.TotalAmount);
        var review = Assert.Single(control.NeedsReview);
        Assert.Equal(redelivery.Id, review.TransportOrderId);
        Assert.Contains(RedeliveryBillingRule.OriginalCharged, review.Reasons);
        Assert.Equal("ORD-0002", review.BillingHoldOrderNumber);
    }

    [Fact]
    public async Task Preflight_StopsADraftThatStillCarriesTheRedelivery()
    {
        using var h = await SeedAsync();
        var redeliveryFirst = h.Order("ORD-0003", 300m);
        var draft = await h.InvoiceAsync(redeliveryFirst.Id);
        // The pair only becomes visible after the draft: the link and the original's price came later.
        var original = h.Order("ORD-0002", 300m);
        redeliveryFirst.RedeliveryOfOrderId = original.Id;
        await h.Db.Context.SaveChangesAsync();

        var preflight = await h.Invoices.PreflightAsync(draft.Id, CancellationToken.None);

        var issue = Assert.Single(preflight!.Issues, i => i.Code == InvoiceIssuePreflight.RedeliveryDuplicate);
        Assert.Contains("ORD-0002", issue.Message);
        Assert.False(preflight.CanIssue);
    }

    [Fact]
    public async Task AnUnpricedRedelivery_IsToldTheOriginalCarriesThePrice_NotToPriceIt()
    {
        using var h = await SeedAsync();
        var (_, redelivery) = h.Pair(redeliveryPrice: null);

        var held = await h.CandidateAsync(redelivery.Id);

        Assert.False(held.IsInvoiceable);
        Assert.Contains("pricing.none", held.InvoiceReadinessReasons);
        Assert.Contains(RedeliveryBillingRule.OriginalCharged, held.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task AnUnpricedOriginal_LeavesThePricedRedeliveryAsTheOneCharge()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair(originalPrice: null);

        Assert.True((await h.CandidateAsync(redelivery.Id)).IsInvoiceable);
        Assert.False((await h.CandidateAsync(original.Id)).IsInvoiceable);
    }

    [Fact]
    public async Task IssuedRedelivery_HoldsAnOriginalThatIsPricedLater()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair(originalPrice: null);
        var invoice = await h.InvoiceAsync(redelivery.Id);
        await IssueDirectlyAsync(h, invoice.Id);

        original.AgreedPrice = 300m;
        await h.Db.Context.SaveChangesAsync();

        var held = await h.CandidateAsync(original.Id);
        Assert.False(held.IsInvoiceable);
        Assert.Contains(RedeliveryBillingRule.RedeliveryCharged, held.InvoiceReadinessReasons);
        Assert.Equal("ORD-0003", held.BillingHoldOrderNumber);
    }

    [Fact]
    public async Task ASecondRedelivery_IsHeldByTheOriginal_ThroughAFreeFirstRedelivery()
    {
        using var h = await SeedAsync();
        var (original, first) = h.Pair();
        first.FreeConfirmed = true;
        first.AgreedPrice = 0m;
        var second = h.Order("ORD-0004", 300m, first.Id);
        await h.Db.Context.SaveChangesAsync();

        var held = await h.CandidateAsync(second.Id);

        Assert.False(held.IsInvoiceable);
        Assert.Equal(original.OrderNumber, held.BillingHoldOrderNumber);
    }

    [Fact]
    public async Task ACancelledRedelivery_NeverHoldsAnything_AndAnOrdinaryOrderIsUntouched()
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        h.Order("ORD-0003", 300m, original.Id, TransportOrderStatus.Cancelled);
        var ordinary = h.Order("ORD-0005", 120m);

        Assert.True((await h.CandidateAsync(original.Id)).IsInvoiceable);
        Assert.True((await h.CandidateAsync(ordinary.Id)).IsInvoiceable);
        Assert.Null((await h.CandidateAsync(ordinary.Id)).BillingHoldOrderNumber);
    }

    [Fact]
    public async Task TheRule_NeverReadsAnotherTenantsOrders()
    {
        using var h = await SeedAsync();
        var foreign = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), OrderNumber = "ANDER-1",
            OrderDate = Day, Status = TransportOrderStatus.Completed, AgreedPrice = 999m,
        };
        var mine = h.Order("ORD-0003", 300m);
        h.Db.Context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF");
        h.Db.Context.TransportOrders.Add(foreign);
        mine.RedeliveryOfOrderId = foreign.Id;
        await h.Db.Context.SaveChangesAsync();

        var holds = await RedeliveryBillingRule.ResolveAsync(h.Db.Context, h.TenantId, [mine.Id], CancellationToken.None);

        Assert.Empty(holds);
    }

    // ------------------------------------------------------------ the decisions that release it

    [Fact]
    public async Task FreeRedelivery_IsAResolvedZeroLine_AndTheInvoiceTotalIsTheOneCharge()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair(redeliveryPrice: null);
        h.Db.Context.TransportOrderPricingLines.Add(new TransportOrderPricingLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = redelivery.Id, Sequence = 1,
            Label = "Herlevering", Amount = 300m, Source = "Manueel", Kind = OrderPriceLineKind.Manual, LineKey = "manual:x",
        });
        h.Db.Context.TransportOrderServiceLines.Add(new TransportOrderServiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = redelivery.Id,
            NameSnapshot = "Wachttijd", Amount = 40m,
        });
        redelivery.AgreedPrice = 340m;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(
            redelivery.Id, new ConfirmOrderFreeRequest("Onze fout"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var candidate = await h.CandidateAsync(redelivery.Id);
        Assert.True(candidate.IsFree);
        Assert.True(candidate.IsInvoiceable);

        var invoice = await h.InvoiceAsync(original.Id, redelivery.Id);
        Assert.Equal(300m, invoice.Lines.Sum(l => l.Quantity * l.UnitPrice));
        Assert.Contains(invoice.Lines, l => l.Description.Contains("ORD-0003") && l.UnitPrice == 0m);
        Assert.False(await h.Db.Context.TransportOrderServiceLines.AnyAsync(l => l.TransportOrderId == redelivery.Id));
    }

    [Fact]
    public async Task FreeOriginal_MakesTheRedeliveryTheOneCharge()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair();

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(
            original.Id, new ConfirmOrderFreeRequest("Niet geleverd, niet aangerekend"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.True((await h.CandidateAsync(redelivery.Id)).IsInvoiceable);
        var invoice = await h.InvoiceAsync(original.Id, redelivery.Id);
        Assert.Equal(300m, invoice.Lines.Sum(l => l.Quantity * l.UnitPrice));
    }

    [Fact]
    public async Task ApprovedIncidentCharge_IsTheIntentionalExtraCharge_AndLandsOnceOnTheOriginal()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair(redeliveryPrice: null);
        var incident = await h.Incidents.CreateAsync(new SaveIncidentRequest(
            "Mislukte levering", "Klant was gesloten.", "Damage", "Medium",
            CustomerId: h.CustomerId, TransportOrderId: original.Id, ResponsibleParty: "Customer"), CancellationToken.None);
        await h.Incidents.ProposeChargeAsync(incident.Id, new ProposeIncidentChargeRequest(75m, "Tweede aanbieding"), CancellationToken.None);
        await h.Incidents.DecideChargeAsync(incident.Id, new DecideIncidentChargeRequest(true), CancellationToken.None);

        await h.Orders.ConfirmFreeAfterExecutionAsync(
            redelivery.Id, new ConfirmOrderFreeRequest("Kost via doorrekening"), CancellationToken.None);
        var invoice = await h.InvoiceAsync(original.Id, redelivery.Id);

        Assert.Equal(375m, invoice.Lines.Sum(l => l.Quantity * l.UnitPrice));
    }

    [Fact]
    public async Task ApprovedCharge_ContradictsFree_SoTheOriginalCannotBeDeclaredFreeAfterwards()
    {
        using var h = await SeedAsync();
        var (original, _) = h.Pair();
        var incident = await h.Incidents.CreateAsync(new SaveIncidentRequest(
            "Mislukte levering", "Klant was gesloten.", "Damage", "Medium",
            CustomerId: h.CustomerId, TransportOrderId: original.Id, ResponsibleParty: "Customer"), CancellationToken.None);
        await h.Incidents.ProposeChargeAsync(incident.Id, new ProposeIncidentChargeRequest(75m, "Tweede aanbieding"), CancellationToken.None);
        await h.Incidents.DecideChargeAsync(incident.Id, new DecideIncidentChargeRequest(true), CancellationToken.None);

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(
            original.Id, new ConfirmOrderFreeRequest("Toch gratis"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task ApprovedCharge_OnAFreeOriginal_EndsTheFreeDecision_SoItIsReallyBilled()
    {
        using var h = await SeedAsync();
        var (original, _) = h.Pair(redeliveryPrice: null);
        await h.Orders.ConfirmFreeAfterExecutionAsync(original.Id, new ConfirmOrderFreeRequest("Gratis"), CancellationToken.None);
        var incident = await h.Incidents.CreateAsync(new SaveIncidentRequest(
            "Mislukte levering", "Klant was gesloten.", "Damage", "Medium",
            CustomerId: h.CustomerId, TransportOrderId: original.Id, ResponsibleParty: "Customer"), CancellationToken.None);
        await h.Incidents.ProposeChargeAsync(incident.Id, new ProposeIncidentChargeRequest(75m, "Tweede aanbieding"), CancellationToken.None);
        await h.Incidents.DecideChargeAsync(incident.Id, new DecideIncidentChargeRequest(true), CancellationToken.None);

        var invoice = await h.InvoiceAsync(original.Id);

        Assert.Equal(75m, invoice.Lines.Sum(l => l.Quantity * l.UnitPrice));
    }

    [Fact]
    public async Task FullCreditOfTheOriginal_ReleasesTheRedelivery_APartialOneDoesNot()
    {
        using var h = await SeedAsync();
        var (original, redelivery) = h.Pair();
        var invoice = await h.InvoiceAsync(original.Id);
        await IssueDirectlyAsync(h, invoice.Id);
        Assert.False((await h.CandidateAsync(redelivery.Id)).IsInvoiceable);

        var note = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, Kind = InvoiceKind.CreditNote,
            CreditedInvoiceId = invoice.Id, Status = InvoiceStatus.Sent, InvoiceDate = Day, DueDate = Day, InvoiceNumber = "CN-1",
        };
        note.Lines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, Description = "deel", Quantity = 1m, UnitPrice = 100m,
            TransportOrderId = original.Id,
        });
        h.Db.Context.Invoices.Add(note);
        await h.Db.Context.SaveChangesAsync();
        Assert.False((await h.CandidateAsync(redelivery.Id)).IsInvoiceable);

        note.Lines.Single().UnitPrice = 300m;
        await h.Db.Context.SaveChangesAsync();
        Assert.True((await h.CandidateAsync(redelivery.Id)).IsInvoiceable);
    }

    [Fact]
    public async Task ACreditNoteDraft_IsNeverJudgedByTheRule()
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        var redelivery = h.Order("ORD-0003", 300m);
        var invoice = await h.InvoiceAsync(redelivery.Id);
        await IssueDirectlyAsync(h, invoice.Id);
        redelivery.RedeliveryOfOrderId = original.Id;
        await h.Db.Context.SaveChangesAsync();

        var credit = await h.Invoices.CreateCreditNoteAsync(invoice.Id, new CreateCreditNoteRequest("Fout"), CancellationToken.None);
        var preflight = await h.Invoices.PreflightAsync(credit.Invoice!.Id, CancellationToken.None);

        Assert.DoesNotContain(preflight!.Issues, i => i.Code == InvoiceIssuePreflight.RedeliveryDuplicate);
    }

    // ------------------------------------------------------------ R-N1: when "gratis" is legal

    [Fact]
    public async Task OneOffPrice_StaysRefusedOnEveryCompletedOrder()
    {
        using var h = await SeedAsync();
        var (_, redelivery) = h.Pair(redeliveryPrice: null);

        var agreed = await h.Orders.SetOneOffPriceAsync(redelivery.Id, new SetOneOffPriceRequest(300m), CancellationToken.None);
        var free = await h.Orders.SetOneOffPriceAsync(redelivery.Id, new SetOneOffPriceRequest(0m, null, FreeConfirmed: true), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, agreed.Outcome);
        Assert.Equal(TransportOrderOperationOutcome.InvalidState, free.Outcome);
    }

    [Fact]
    public async Task ConfirmFree_IsRefused_OnAnOrdinaryCompletedOrder()
    {
        using var h = await SeedAsync();
        var ordinary = h.Order("ORD-0005", null);

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(ordinary.Id, new ConfirmOrderFreeRequest("x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        Assert.False((await h.Orders.GetByIdAsync(ordinary.Id, CancellationToken.None))!.PostExecutionFreeAllowed);
    }

    [Theory]
    [InlineData(TransportOrderStatus.Draft)]
    [InlineData(TransportOrderStatus.InProgress)]
    [InlineData(TransportOrderStatus.Invoiced)]
    public async Task ConfirmFree_IsRefused_OutsideTheCompletedStatus(TransportOrderStatus status)
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        var redelivery = h.Order("ORD-0003", null, original.Id, status);

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }

    [Theory]
    [InlineData(OrderPricingStatus.Locked)]
    [InlineData(OrderPricingStatus.Invoiced)]
    public async Task ConfirmFree_RespectsALockedOrInvoicedPrice(OrderPricingStatus pricing)
    {
        using var h = await SeedAsync();
        var (_, redelivery) = h.Pair();
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = redelivery.Id, Status = pricing,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        await h.ReloadAsync(redelivery);
        Assert.Equal(300m, redelivery.AgreedPrice);
    }

    [Fact]
    public async Task ConfirmFree_IsRefused_OnAnOrderAlreadyOnAnInvoice()
    {
        using var h = await SeedAsync();
        var (original, _) = h.Pair();
        await h.InvoiceAsync(original.Id);
        original.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Orders.ConfirmFreeAfterExecutionAsync(original.Id, new ConfirmOrderFreeRequest("x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task ConfirmFree_NeedsTheRightAndAReason_AndHonoursTheVersion()
    {
        using var h = await SeedAsync();
        var (_, redelivery) = h.Pair(redeliveryPrice: null);

        var noReason = await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("  "), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noReason.Outcome);

        var stale = await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("x", Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.VersionConflict, stale.Outcome);

        h.Permissions.Codes.Clear();
        var noRight = await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("x"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noRight.Outcome);
        await h.ReloadAsync(redelivery);
        Assert.False(redelivery.FreeConfirmed);
    }

    [Fact]
    public async Task ASalesLineAfterFree_EndsTheFreeDecision_ButTheOriginalStillHoldsTheRedelivery()
    {
        using var h = await SeedAsync();
        var (_, redelivery) = h.Pair(redeliveryPrice: null);
        await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("Gratis"), CancellationToken.None);

        var result = await h.Orders.SaveOrderPriceLinesAsync(redelivery.Id,
            [new SaveOrderPriceLineRequest(null, "Herlevering", null, null, 300m, null)], CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        await h.ReloadAsync(redelivery);
        Assert.False(redelivery.FreeConfirmed);
        Assert.Equal(300m, redelivery.AgreedPrice);
        Assert.False((await h.CandidateAsync(redelivery.Id)).IsInvoiceable);
    }

    [Fact]
    public async Task Detail_CarriesTheVerdict_ForTheUi()
    {
        using var h = await SeedAsync();
        var (_, redelivery) = h.Pair(redeliveryPrice: null);

        var detail = await h.Orders.GetByIdAsync(redelivery.Id, CancellationToken.None);

        Assert.True(detail!.PostExecutionFreeAllowed);
        Assert.Equal(RedeliveryBillingRule.OriginalCharged, detail.BillingHoldReason);
        Assert.Equal("ORD-0002", detail.BillingHoldOrderNumber);

        await h.Orders.ConfirmFreeAfterExecutionAsync(redelivery.Id, new ConfirmOrderFreeRequest("Gratis"), CancellationToken.None);
        detail = await h.Orders.GetByIdAsync(redelivery.Id, CancellationToken.None);
        Assert.False(detail!.PostExecutionFreeAllowed);
        Assert.Null(detail.BillingHoldReason);
    }

    // ------------------------------------------------------------ failed stop / POD

    private static async Task<Guid> ExecutedOrderAsync(Harness h, TransportOrder order, StopExecutionStatus status)
    {
        var stop = new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = order.Id, Sequence = 1,
            StopType = StopType.Unloading, City = "Hasselt", CountryCode = "BE",
        };
        var trip = new Trip { Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-" + order.OrderNumber, TripDate = Day, Status = TripStatus.Completed };
        h.Db.Context.AddRange(stop, trip,
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = order.Id, Sequence = 1 },
            new StopExecution { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderStopId = stop.Id, Status = status, CompletedAt = Now.UtcDateTime, StatusReason = "gesloten" });
        await h.Db.Context.SaveChangesAsync();
        return stop.Id;
    }

    [Fact]
    public async Task FailedStop_WithoutARedelivery_StaysNakijken_NeverSilentlyBillable()
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        await ExecutedOrderAsync(h, original, StopExecutionStatus.Failed);

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, original, CancellationToken.None);

        Assert.Equal("pod.missing", original.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task FailedStop_WithAPlannedRedelivery_StaysNakijken()
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        await ExecutedOrderAsync(h, original, StopExecutionStatus.Failed);
        h.Order("ORD-0003", null, original.Id, TransportOrderStatus.Planned);

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, original, CancellationToken.None);

        Assert.Equal("pod.missing", original.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task CompletingTheRedelivery_SettlesTheProofOfTheFailedOriginal()
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        await ExecutedOrderAsync(h, original, StopExecutionStatus.Failed);
        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, original, CancellationToken.None);
        var redelivery = h.Order("ORD-0003", null, original.Id, TransportOrderStatus.InProgress);

        // Not saved yet: the trip-completion flow evaluates before its own save.
        redelivery.Status = TransportOrderStatus.Completed;
        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, redelivery, CancellationToken.None);

        Assert.Equal(InvoiceReadinessEvaluator.ReadyForInvoice, original.InvoiceReadiness);
        Assert.Null(original.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task ACompletedStop_StillNeedsItsOwnProof_EvenWithACompletedRedelivery()
    {
        using var h = await SeedAsync();
        var original = h.Order("ORD-0002", 300m);
        await ExecutedOrderAsync(h, original, StopExecutionStatus.Completed);
        h.Order("ORD-0003", null, original.Id);

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, original, CancellationToken.None);

        Assert.Equal("pod.missing", original.InvoiceReadinessReasons);
    }

    private static async Task IssueDirectlyAsync(Harness h, Guid invoiceId)
    {
        var invoice = await h.Db.Context.Invoices.SingleAsync(i => i.Id == invoiceId);
        invoice.Status = InvoiceStatus.Sent;
        invoice.InvoiceNumber = "FAC-" + invoiceId.ToString("N")[..6];
        await h.Db.Context.SaveChangesAsync();
    }
}
