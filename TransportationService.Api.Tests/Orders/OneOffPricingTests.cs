using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Phase 6: one-off order pricing (no contract consulted), the shared included-time helper and
/// the extra-time proposal it produces for both one-off orders and engaged contract agreements.
/// </summary>
public class OneOffPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Grants only the permission codes explicitly added by a test (fail-closed default, matching production).</summary>
    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        var permissions = new PermissionSet();
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, permissions);
        return new Harness(db, sut, admin, permissions, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest OneOffRequest(
        Guid customerId, decimal fixedAmount,
        int? includedLoading = null, int? includedUnloading = null, int? includedCombined = null,
        decimal? extraHourlyRate = null, string? notes = null) => new(
        customerId, "REF-1", new DateOnly(2026, 7, 26), "Meubels", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        PricingSource: OrderPricingSource.OneOff,
        OneOffFixedAmount: fixedAmount,
        OneOffIncludedLoadingMinutes: includedLoading,
        OneOffIncludedUnloadingMinutes: includedUnloading,
        OneOffIncludedCombinedMinutes: includedCombined,
        OneOffExtraHourlyRate: extraHourlyRate,
        OneOffNotes: notes);

    private static CreateTransportOrderRequest ContractRequest(Guid customerId, decimal quantity = 3) => new(
        customerId, "REF-2", new DateOnly(2026, 7, 26), "Pallets", quantity, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        QuantityUnitCode: "EUROPALLET");

    /// <summary>
    /// Task 10 test fixture: an engaged customer agreement (a PriceRule on the pallet unit ties it
    /// to the order built by <see cref="ContractRequest"/>) carrying the given included-time
    /// configuration, so extra-time proposals have a winning agreement to compare against.
    /// </summary>
    private static async Task<Guid> CreateEngagedAgreementAsync(
        Harness h, string name, int? includedLoadingMinutes = null, int? includedUnloadingMinutes = null,
        int? includedCombinedMinutes = null, decimal? extraHourlyRate = null)
    {
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, name, new DateOnly(2026, 1, 1), null, true,
            null, null, null,
            IncludedLoadingMinutes: includedLoadingMinutes, IncludedUnloadingMinutes: includedUnloadingMinutes,
            IncludedCombinedMinutes: includedCombinedMinutes, ExtraHourlyRate: extraHourlyRate), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            $"Pallets {name}", new DateOnly(2026, 1, 1), null, true, 30m, null, null,
            AgreementId: agreement.Id), CancellationToken.None);
        return agreement.Id;
    }

    /// <summary>Seeds a Trip + StopExecution rows so the order's actual loading/unloading minutes can be measured.</summary>
    private static async Task SeedStopExecutionsAsync(
        Harness h, Guid orderId, int? loadingMinutes, int? unloadingMinutes)
    {
        var order = await h.Db.Context.TransportOrders.Include(o => o.Stops)
            .SingleAsync(o => o.Id == orderId);
        var loadStop = order.Stops.Single(s => s.StopType == StopType.Loading);
        var unloadStop = order.Stops.Single(s => s.StopType == StopType.Unloading);

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0001", TripDate = new DateOnly(2026, 7, 26),
            Status = TripStatus.InProgress,
        });

        var arrived = new DateTime(2026, 7, 26, 8, 0, 0, DateTimeKind.Utc);
        if (loadingMinutes is { } lm)
        {
            h.Db.Context.StopExecutions.Add(new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = loadStop.Id,
                Status = StopExecutionStatus.Completed, ArrivedAt = arrived, DepartedAt = arrived.AddMinutes(lm),
            });
        }

        if (unloadingMinutes is { } um)
        {
            h.Db.Context.StopExecutions.Add(new StopExecution
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = unloadStop.Id,
                Status = StopExecutionStatus.Completed, ArrivedAt = arrived, DepartedAt = arrived.AddMinutes(um),
            });
        }

        await h.Db.Context.SaveChangesAsync();
    }

    /// <summary>
    /// Reruns pricing on the order's CURRENT stops (invoking the private ApplyPricingAsync via
    /// reflection) without the public UpdateAsync's wholesale stop replacement — which would
    /// regenerate stop ids and orphan any StopExecution rows seeded against them. This mirrors
    /// what a future "recalculate after execution" trigger would do (spec: proposed charges are
    /// only ever visible after a resave, never silently recomputed on read).
    /// </summary>
    private static async Task<TransportOrderDetailDto> RepriceAsync(Harness h, Guid orderId)
    {
        var order = await h.Db.Context.TransportOrders.Include(o => o.Stops).SingleAsync(o => o.Id == orderId);
        var method = typeof(TransportOrderService).GetMethod("ApplyPricingAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<TransportOrderOperationResult?>)method.Invoke(h.Sut, new object?[]
        {
            order, order.AgreedPrice, Array.Empty<OrderServiceInput>(),
            false, null, null, CancellationToken.None,
        })!;
        var error = await task;
        Assert.Null(error);
        await h.Db.Context.SaveChangesAsync();
        return await h.Sut.GetByIdAsync(orderId, CancellationToken.None) ?? throw new InvalidOperationException("Order not found.");
    }

    /// <summary>
    /// Task 10: sets the order-level included-time override fields directly on the tracked entity
    /// and saves — bypassing the public UpdateAsync, which wholesale-replaces stops (see
    /// RepriceAsync's doc comment) and would orphan any StopExecution rows seeded against them.
    /// Used by tests that only care about the engine's resolution/rounding/minimum behaviour, not
    /// the update endpoint's own validation/audit (covered separately, via UpdateAsync, below).
    /// </summary>
    private static async Task SetIncludedTimeOverridesAsync(
        Harness h, Guid orderId,
        int? includedLoading = null, int? includedUnloading = null, decimal? extraHourlyRate = null,
        int? roundingStepMinutes = null, int? minimumBillableMinutes = null)
    {
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
        order.IncludedLoadingMinutesOverride = includedLoading;
        order.IncludedUnloadingMinutesOverride = includedUnloading;
        order.ExtraTimeHourlyRateOverride = extraHourlyRate;
        order.ExtraTimeRoundingStepMinutes = roundingStepMinutes;
        order.ExtraTimeMinimumBillableMinutes = minimumBillableMinutes;
        await h.Db.Context.SaveChangesAsync();
    }

    /// <summary>Rebuilds an UpdateTransportOrderRequest from a detail DTO (spec: round-trip a loaded order through the update endpoint).</summary>
    private static UpdateTransportOrderRequest BuildUpdateFrom(TransportOrderDetailDto d) => new(
        d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
        d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
        d.AgreedPrice, d.Notes,
        d.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
            .ToList(),
        QuantityUnitCode: d.QuantityUnitCode,
        PriceIsManual: d.PriceIsManual, PriceOverrideReason: d.PriceOverrideReason,
        PricingSource: d.PricingSource, OneOffFixedAmount: d.OneOffFixedAmount,
        OneOffIncludedLoadingMinutes: d.OneOffIncludedLoadingMinutes,
        OneOffIncludedUnloadingMinutes: d.OneOffIncludedUnloadingMinutes,
        OneOffIncludedCombinedMinutes: d.OneOffIncludedCombinedMinutes,
        OneOffExtraHourlyRate: d.OneOffExtraHourlyRate, OneOffNotes: d.OneOffNotes,
        IncludedLoadingMinutesOverride: d.IncludedLoadingMinutesOverride,
        IncludedUnloadingMinutesOverride: d.IncludedUnloadingMinutesOverride,
        ExtraTimeHourlyRateOverride: d.ExtraTimeHourlyRateOverride,
        ExtraTimeRoundingStepMinutes: d.ExtraTimeRoundingStepMinutes,
        ExtraTimeMinimumBillableMinutes: d.ExtraTimeMinimumBillableMinutes);

    // --- S10: one-off order, no contract at all ---------------------------------------------

    [Fact]
    public async Task OneOff_NoPricingConfig_PricesTheFixedAmount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(OneOffRequest(h.CustomerId, 850m), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(850m, created.Order!.AgreedPrice);
        Assert.Equal(850m, created.Order.CalculatedPrice);
        Assert.False(created.Order.PriceIsManual);
        Assert.NotNull(created.Order.PricingSnapshot);
        Assert.Contains(created.Order.PricingLines!, l => l.Label == "Eenmalige prijsafspraak" && l.Amount == 850m);
    }

    [Fact]
    public async Task OneOff_OnCustomerWithContract_DoesNotTouchContractRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets klant X", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);

        var oneOff = await h.Sut.CreateAsync(OneOffRequest(h.CustomerId, 850m), CancellationToken.None);
        Assert.Equal(850m, oneOff.Order!.AgreedPrice);

        // Another order for the SAME customer still prices through the untouched contract.
        var contract = await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 3), CancellationToken.None);
        Assert.Equal(90m, contract.Order!.AgreedPrice);
        Assert.Contains(contract.Order.PricingLines!, l => l.Source == "Pallets klant X");
    }

    // --- S11: separate included time, one side over the allowance ---------------------------

    [Fact]
    public async Task OneOff_SeparateIncludedTime_OverLoadingAllowance_ProposesExtraCharge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: 75m),
            CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 60, unloadingMinutes: 30);
        // Pricing only reruns on save — reprice now that executions exist.
        var reloaded = await RepriceAsync(h, created.Order.Id);

        var proposed = Assert.Single(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal("Extra laadtijd: 60 min (inbegrepen 30 min)", proposed.Label);
        Assert.Equal(37.50m, proposed.Amount);
        // Unloading (30 actual vs 30 included) produced no extra line.
        Assert.DoesNotContain(reloaded.PricingLines!, l => l.Label.Contains("lostijd"));

        Assert.Equal(450m, reloaded.AgreedPrice);
        Assert.Equal(450m, reloaded.CalculatedPrice);
        Assert.Equal(487.50m, reloaded.TotalWithProposed);
    }

    // --- S12: combined included time -----------------------------------------------------------

    [Fact]
    public async Task OneOff_CombinedIncludedTime_OverAllowance_ProposesExtraCharge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedCombined: 60, extraHourlyRate: 75m),
            CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 45, unloadingMinutes: 45);
        var reloaded = await RepriceAsync(h, created.Order.Id);

        var proposed = Assert.Single(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal("Extra laad-/lostijd: 90 min (inbegrepen 60 min)", proposed.Label);
        Assert.Equal(37.50m, proposed.Amount);
        Assert.Equal(487.50m, reloaded.TotalWithProposed);
    }

    /// <summary>§6 item 8: a Proposed line is excluded from LinesTotal/AgreedPrice until confirmed, then both increase by exactly its amount.</summary>
    [Fact]
    public async Task ConfirmOrderPriceLine_FlipsProposedToAuto_IncreasesLinesTotalAndAgreedPriceByItsAmount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedCombined: 60, extraHourlyRate: 75m),
            CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 45, unloadingMinutes: 45);
        var reloaded = await RepriceAsync(h, created.Order.Id);

        var proposed = Assert.Single(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal(OrderPriceLineKind.Proposed, proposed.Kind);
        Assert.Equal(37.50m, proposed.Amount);
        // Not yet counted: LinesTotal/AgreedPrice still just the base one-off amount.
        Assert.Equal(450m, reloaded.PricingSnapshot!.LinesTotal);
        Assert.Equal(450m, reloaded.AgreedPrice);

        var confirmed = await h.Sut.ConfirmOrderPriceLineAsync(created.Order.Id, proposed.Id!.Value, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);
        var confirmedLine = Assert.Single(confirmed.Order!.PricingLines!, l => l.LineKey == proposed.LineKey);
        Assert.Equal(OrderPriceLineKind.Auto, confirmedLine.Kind);
        Assert.False(confirmedLine.Proposed);
        Assert.Equal(37.50m, confirmedLine.Amount); // unchanged by the confirm
        Assert.Equal(487.50m, confirmed.Order.PricingSnapshot!.LinesTotal); // +37.50
        Assert.Equal(487.50m, confirmed.Order.AgreedPrice); // +37.50
    }

    [Fact]
    public async Task OneOff_NoStopExecutions_ProducesNoExtraTimeLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: 75m),
            CancellationToken.None);

        Assert.DoesNotContain(created.Order!.PricingLines!, l => l.Proposed);
        Assert.Equal(450m, created.Order.AgreedPrice);
        Assert.Equal(450m, created.Order.TotalWithProposed);
    }

    [Fact]
    public async Task OneOff_ExtraTimeWithoutRate_IsInformational_NeverCharged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: null),
            CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 60, unloadingMinutes: null);
        var reloaded = await RepriceAsync(h, created.Order.Id);

        var informational = Assert.Single(reloaded.PricingLines!, l => l.Informational && l.Source == "Extra tijd");
        Assert.Equal("Extra tijd: geef het uurtarief voor extra tijd op", informational.Label);
        Assert.Equal(0m, informational.Amount);
        Assert.DoesNotContain(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal(450m, reloaded.AgreedPrice);
    }

    // --- Validation ---------------------------------------------------------------------------

    [Fact]
    public async Task OneOff_WithoutFixedAmount_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 0m) with { OneOffFixedAmount = null }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task OneOff_CombinedAndSeparateIncludedTime_BothSet_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedCombined: 60), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Agreement_CombinedAndSeparateIncludedTime_BothSet_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(
                h.CustomerId, "Contract X", new DateOnly(2026, 1, 1), null, true,
                null, null, null,
                IncludedLoadingMinutes: 30, IncludedCombinedMinutes: 60),
            CancellationToken.None));
    }

    // --- S18 variant: one-off snapshot is immune to later tariff/contract changes ------------

    [Fact]
    public async Task OneOff_Snapshot_Unaffected_ByLaterContractChangeAndResave()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var oneOff = await h.Sut.CreateAsync(OneOffRequest(h.CustomerId, 500m), CancellationToken.None);
        Assert.Equal(500m, oneOff.Order!.AgreedPrice);

        // A contract rule appears afterwards and another order is saved through it.
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets klant X", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 2), CancellationToken.None);

        var reloaded = await h.Sut.GetByIdAsync(oneOff.Order.Id, CancellationToken.None);
        Assert.Equal(500m, reloaded!.AgreedPrice);
        Assert.Equal(OrderPricingSource.OneOff, reloaded.PricingSource);
        Assert.Contains("Eenmalige prijsafspraak", reloaded.PricingSnapshot!.Explanation);
    }

    // --- Tenant isolation on the StopExecution actuals query -----------------------------------

    [Fact]
    public async Task ActualsQuery_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: 75m),
            CancellationToken.None);
        var order = await h.Db.Context.TransportOrders.Include(o => o.Stops).SingleAsync(o => o.Id == created.Order!.Id);
        var loadStop = order.Stops.Single(s => s.StopType == StopType.Loading);

        // A different tenant's StopExecution against the SAME stop id must never be summed in.
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        var otherTripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = otherTripId, TenantId = otherTenantId, TripNumber = "RIT-9999", TripDate = new DateOnly(2026, 7, 26),
            Status = TripStatus.InProgress,
        });
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, TripId = otherTripId, TransportOrderStopId = loadStop.Id,
            Status = StopExecutionStatus.Completed,
            ArrivedAt = new DateTime(2026, 7, 26, 8, 0, 0, DateTimeKind.Utc),
            DepartedAt = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc), // 240 minutes — would dwarf any real reading
        });
        await h.Db.Context.SaveChangesAsync();

        // Reprice (pricing only reruns on save) — the other tenant's 240-minute row must not surface.
        var reloaded = await RepriceAsync(h, created.Order!.Id);
        Assert.DoesNotContain(reloaded.PricingLines!, l => l.Proposed);
    }

    // --- Carried-over Phase 6 review items (mandatory in Phase 7, spec ch. 24-26 §8) -----------

    /// <summary>A Failed execution still gets CompletedAt stamped but never counts as billable dwell time.</summary>
    [Fact]
    public async Task FailedStopExecution_NeverCountsAsActualDwellTime_ProducesNoExtraTimeProposal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: 75m),
            CancellationToken.None);
        var order = await h.Db.Context.TransportOrders.Include(o => o.Stops).SingleAsync(o => o.Id == created.Order!.Id);
        var loadStop = order.Stops.Single(s => s.StopType == StopType.Loading);

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0002", TripDate = new DateOnly(2026, 7, 26),
            Status = TripStatus.InProgress,
        });
        var arrived = new DateTime(2026, 7, 26, 8, 0, 0, DateTimeKind.Utc);
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = loadStop.Id,
            Status = StopExecutionStatus.Failed, ArrivedAt = arrived, CompletedAt = arrived.AddMinutes(240),
        });
        await h.Db.Context.SaveChangesAsync();

        var reloaded = await RepriceAsync(h, created.Order!.Id);

        Assert.DoesNotContain(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal(450m, reloaded.AgreedPrice);
    }

    /// <summary>A Skipped execution is excluded the same way as Failed.</summary>
    [Fact]
    public async Task SkippedStopExecution_NeverCountsAsActualDwellTime()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: 75m),
            CancellationToken.None);
        var order = await h.Db.Context.TransportOrders.Include(o => o.Stops).SingleAsync(o => o.Id == created.Order!.Id);
        var loadStop = order.Stops.Single(s => s.StopType == StopType.Loading);

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0003", TripDate = new DateOnly(2026, 7, 26),
            Status = TripStatus.InProgress,
        });
        var arrived = new DateTime(2026, 7, 26, 8, 0, 0, DateTimeKind.Utc);
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = loadStop.Id,
            Status = StopExecutionStatus.Skipped, ArrivedAt = arrived, DepartedAt = arrived.AddMinutes(120),
        });
        await h.Db.Context.SaveChangesAsync();

        var reloaded = await RepriceAsync(h, created.Order!.Id);

        Assert.DoesNotContain(reloaded.PricingLines!, l => l.Proposed);
    }

    /// <summary>Two engaged agreements both configuring included time must never double-count/double-propose; the most specific one wins.</summary>
    [Fact]
    public async Task TwoEngagedAgreements_WithIncludedTime_OnlyTheMostSpecificApplies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var containerUnitId = Guid.NewGuid();
        h.Db.Context.UnitTypes.Add(new UnitType { Id = containerUnitId, TenantId = h.TenantId, Code = "CONTAINER", Name = "Container", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        // Private agreement (tier 2) — must win over the shared/assigned one (tier 1).
        var privateAgreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Privé contract", new DateOnly(2026, 1, 1), null, true,
            null, null, null, IncludedCombinedMinutes: 60, ExtraHourlyRate: 75m), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null, "Pallets privé", new DateOnly(2026, 1, 1), null, true,
            30m, null, null, AgreementId: privateAgreement.Id), CancellationToken.None);

        var sharedAgreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Gedeelde tabel", new DateOnly(2026, 1, 1), null, true,
            null, null, null, IsShared: true, IncludedCombinedMinutes: 30, ExtraHourlyRate: 50m), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, containerUnitId, PriceRuleBasis.PerUnit, null, "Containers gedeeld", new DateOnly(2026, 1, 1), null, true,
            50m, null, null, AgreementId: sharedAgreement.Id), CancellationToken.None);
        await h.Admin.SaveAssignmentsAsync(sharedAgreement.Id,
            [new SavePricingAssignmentRequest(h.CustomerId, null, null, null, null, null)], CancellationToken.None);

        var engine = new PricingEngine(h.Db.Context, new DevTenantContext(h.TenantId));
        var result = await engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerId, new DateOnly(2026, 7, 26),
            [new PriceCalculationLineInput(h.PalletUnitId, 3), new PriceCalculationLineInput(containerUnitId, 1)],
            "BE", "3500", null, null, null, [],
            ActualLoadingMinutes: 45m, ActualUnloadingMinutes: 45m), CancellationToken.None);

        var proposed = result.Lines.Where(l => l.Proposed).ToList();
        var proposedLine = Assert.Single(proposed); // never two proposed lines for the same measured minutes
        Assert.Equal(37.50m, proposedLine.Amount); // (90 - 60) min / 60 × 75 (private agreement's rate, not the shared one's 50)
    }

    /// <summary>Two PRIVATE (same-tier) agreements both configuring included time is a blocking configuration error, never a double proposal.</summary>
    [Fact]
    public async Task TwoEngagedAgreements_WithIncludedTime_AtTheSameTier_IsConfigurationError_NoDoubleProposal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var containerUnitId = Guid.NewGuid();
        h.Db.Context.UnitTypes.Add(new UnitType { Id = containerUnitId, TenantId = h.TenantId, Code = "CONTAINER", Name = "Container", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        // Both private (tier 2) — an exact tie, unlike the private-vs-shared case above.
        var firstAgreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Contract A", new DateOnly(2026, 1, 1), null, true,
            null, null, null, IncludedCombinedMinutes: 60, ExtraHourlyRate: 75m), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null, "Pallets A", new DateOnly(2026, 1, 1), null, true,
            30m, null, null, AgreementId: firstAgreement.Id), CancellationToken.None);

        var secondAgreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Contract B", new DateOnly(2026, 1, 1), null, true,
            null, null, null, IncludedCombinedMinutes: 30, ExtraHourlyRate: 50m), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, containerUnitId, PriceRuleBasis.PerUnit, null, "Containers B", new DateOnly(2026, 1, 1), null, true,
            50m, null, null, AgreementId: secondAgreement.Id), CancellationToken.None);

        var engine = new PricingEngine(h.Db.Context, new DevTenantContext(h.TenantId));
        var result = await engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerId, new DateOnly(2026, 7, 26),
            [new PriceCalculationLineInput(h.PalletUnitId, 3), new PriceCalculationLineInput(containerUnitId, 1)],
            "BE", "3500", null, null, null, [],
            ActualLoadingMinutes: 45m, ActualUnloadingMinutes: 45m), CancellationToken.None);

        Assert.True(result.RequiresManualPrice);
        Assert.NotNull(result.ConfigurationError);
        Assert.Contains("Meerdere prijsafspraken met inbegrepen tijd", result.ConfigurationError);
        Assert.DoesNotContain(result.Lines, l => l.Proposed); // never a proposal from either side of the tie
        Assert.Contains(result.Lines, l => l.Source == "Configuratiefout" && l.Amount == 0m);
    }

    /// <summary>Loading AND unloading both exceeding the allowance with no configured rate emits ONE informational line, not two.</summary>
    [Fact]
    public async Task BothActivitiesExceedAllowance_NoRateConfigured_EmitsOneInformationalLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedLoading: 30, includedUnloading: 30, extraHourlyRate: null),
            CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 60, unloadingMinutes: 90);
        var reloaded = await RepriceAsync(h, created.Order.Id);

        var informational = reloaded.PricingLines!.Where(l => l.Informational && l.Source == "Extra tijd").ToList();
        Assert.Single(informational);
    }

    /// <summary>
    /// Ledger cleanup (Phase 10): TransportOrderPricingLine.Proposed duplicates Kind == Proposed —
    /// Kind is authoritative, Proposed is derived (see the entity's doc comment and
    /// TransportOrderService.SetKind). Adjusting a Proposed line via the manual-edit endpoint must
    /// flip Kind to AutoAdjusted AND clear Proposed in the same write, and every persisted line must
    /// keep satisfying Proposed == (Kind == Proposed) through a subsequent recalculation.
    /// </summary>
    [Fact]
    public async Task AdjustingAProposedLine_ClearsProposed_AndKindProposedInvariantHoldsThroughRecalculate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            OneOffRequest(h.CustomerId, 450m, includedCombined: 60, extraHourlyRate: 75m),
            CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 45, unloadingMinutes: 45);
        var reloaded = await RepriceAsync(h, created.Order.Id);
        var proposed = Assert.Single(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal(OrderPriceLineKind.Proposed, proposed.Kind);

        var adjusted = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order.Id,
            [new SaveOrderPriceLineRequest(proposed.LineKey, proposed.Label, null, null, 50m, "Herzien vóór bevestiging")],
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, adjusted.Outcome);
        var adjustedLine = Assert.Single(adjusted.Order!.PricingLines!, l => l.LineKey == proposed.LineKey);
        Assert.Equal(OrderPriceLineKind.AutoAdjusted, adjustedLine.Kind);
        Assert.False(adjustedLine.Proposed); // must never stay true once Kind left Proposed
        foreach (var line in adjusted.Order.PricingLines!)
        {
            Assert.Equal(line.Kind == OrderPriceLineKind.Proposed, line.Proposed);
        }

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, recalculated.Outcome);
        var survivingAdjusted = Assert.Single(recalculated.Order!.PricingLines!, l => l.LineKey == proposed.LineKey);
        Assert.Equal(OrderPriceLineKind.AutoAdjusted, survivingAdjusted.Kind);
        Assert.False(survivingAdjusted.Proposed);
        foreach (var line in recalculated.Order.PricingLines!)
        {
            Assert.Equal(line.Kind == OrderPriceLineKind.Proposed, line.Proposed);
        }
    }

    // --- Contract mode: engaged agreement with included time (helper reused) ------------------

    [Fact]
    public async Task ContractAgreement_WithIncludedCombinedTime_ProposesExtraChargeAfterSurcharges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Contract X", new DateOnly(2026, 1, 1), null, true,
            null, null, [new SavePricingAgreementSurchargeRequest("Handling", SurchargeKind.Fixed, 10m)],
            IncludedCombinedMinutes: 60, ExtraHourlyRate: 75m), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets contract X", new DateOnly(2026, 1, 1), null, true, 30m, null, null,
            AgreementId: agreement.Id), CancellationToken.None);

        var engine = new PricingEngine(h.Db.Context, new DevTenantContext(h.TenantId));
        var result = await engine.CalculateAsync(new PriceCalculationRequest(
            h.CustomerId, new DateOnly(2026, 7, 26),
            [new PriceCalculationLineInput(h.PalletUnitId, 3)],
            "BE", "3500", null, null, null, [],
            ActualLoadingMinutes: 45m, ActualUnloadingMinutes: 45m), CancellationToken.None);

        Assert.Equal(100m, result.Total); // 3 × 30 + 10 surcharge; proposed excluded
        var surchargeIndex = result.Lines.ToList().FindIndex(l => l.Label == "Handling");
        var proposedIndex = result.Lines.ToList().FindIndex(l => l.Proposed);
        Assert.True(proposedIndex > surchargeIndex);
        Assert.Equal(37.50m, result.Lines[proposedIndex].Amount);
        Assert.Equal(137.50m, result.TotalWithProposed);
    }

    // --- Task 10: order-level included loading/unloading time overrides -----------------------

    /// <summary>The order override wins over the contract's included minutes, per activity.</summary>
    [Fact]
    public async Task OrderOverride_IncludedLoadingMinutes_ReducesOrRemovesExtraTimeProposal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateEngagedAgreementAsync(h, "Contract Override 1", includedLoadingMinutes: 30, extraHourlyRate: 75m);

        var created = await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 3), CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 50, unloadingMinutes: null);
        var reloaded = await RepriceAsync(h, created.Order.Id);

        var proposed = Assert.Single(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal("Extra laadtijd: 50 min (inbegrepen 30 min)", proposed.Label);
        Assert.Equal(25.00m, proposed.Amount); // (50 - 30) / 60 × 75

        // Order override raises the included allowance to 60 min — actual 50 min no longer exceeds it.
        await SetIncludedTimeOverridesAsync(h, created.Order.Id, includedLoading: 60);
        var overridden = await RepriceAsync(h, created.Order.Id);

        Assert.DoesNotContain(overridden.PricingLines!, l => l.Proposed);
    }

    /// <summary>The order's rounding step and minimum billable minutes apply on top of the raw excess.</summary>
    [Fact]
    public async Task OrderOverride_RoundingAndMinimum_ApplyToExtraTime()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateEngagedAgreementAsync(h, "Contract Override 2", includedLoadingMinutes: 30, extraHourlyRate: 60m);

        var created = await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 3), CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 47, unloadingMinutes: null);
        await SetIncludedTimeOverridesAsync(h, created.Order.Id, roundingStepMinutes: 15, minimumBillableMinutes: 30);
        var reloaded = await RepriceAsync(h, created.Order.Id);

        // raw = 47 - 30 = 17 -> ceil(17/15)×15 = 30 -> max(30, minimum 30) = 30 billed minutes.
        var proposed = Assert.Single(reloaded.PricingLines!, l => l.Proposed);
        Assert.Equal("Extra laadtijd: 47 min (inbegrepen 30 min)", proposed.Label);
        Assert.Equal(30.00m, proposed.Amount); // 30 / 60 × 60
    }

    /// <summary>
    /// Wave 2026-08-04 §18: a stop-level included-minutes override wins over the order-level
    /// override (resolution stop → order → contract); clearing it falls back to the order value.
    /// </summary>
    [Fact]
    public async Task StopOverride_WinsOverOrderOverride_AndFallsBackWhenCleared()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateEngagedAgreementAsync(h, "Contract Stopoverride", includedLoadingMinutes: 30, extraHourlyRate: 60m);

        var created = await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 3), CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 50, unloadingMinutes: null);
        await SetIncludedTimeOverridesAsync(h, created.Order.Id, includedLoading: 40);

        // Stop override 60 min beats the order's 40 min — actual 50 min no longer exceeds it.
        var withStops = await h.Db.Context.TransportOrders.Include(o => o.Stops)
            .SingleAsync(o => o.Id == created.Order.Id);
        var loadingStop = withStops.Stops.Single(s => s.StopType == StopType.Loading);
        loadingStop.IncludedTimeMinutesOverride = 60;
        await h.Db.Context.SaveChangesAsync();
        var overridden = await RepriceAsync(h, created.Order.Id);
        Assert.DoesNotContain(overridden.PricingLines!, l => l.Proposed);
        Assert.Equal(60, overridden.Stops.Single(s => s.StopType == StopType.Loading).IncludedTimeMinutesOverride);

        // Clearing the stop override falls back to the order override: 50 − 40 = 10 min → €10.
        loadingStop.IncludedTimeMinutesOverride = null;
        await h.Db.Context.SaveChangesAsync();
        var fallback = await RepriceAsync(h, created.Order.Id);
        var proposed = Assert.Single(fallback.PricingLines!, l => l.Proposed);
        Assert.Equal(10.00m, proposed.Amount); // (50 - 40) / 60 × 60
    }

    /// <summary>Clearing the override (setting it back to null) restores the plain contract-based proposal.</summary>
    [Fact]
    public async Task OrderOverride_Reset_ReturnsToContractValues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateEngagedAgreementAsync(h, "Contract Override 3", includedLoadingMinutes: 30, extraHourlyRate: 75m);

        var created = await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 3), CancellationToken.None);
        await SeedStopExecutionsAsync(h, created.Order!.Id, loadingMinutes: 50, unloadingMinutes: null);
        var baseline = await RepriceAsync(h, created.Order.Id);
        var baselineProposed = Assert.Single(baseline.PricingLines!, l => l.Proposed);
        Assert.Equal(25.00m, baselineProposed.Amount);

        await SetIncludedTimeOverridesAsync(h, created.Order.Id, includedLoading: 60);
        var overridden = await RepriceAsync(h, created.Order.Id);
        Assert.DoesNotContain(overridden.PricingLines!, l => l.Proposed);

        await SetIncludedTimeOverridesAsync(h, created.Order.Id); // every field null = cleared
        var reset = await RepriceAsync(h, created.Order.Id);

        var resetProposed = Assert.Single(reset.PricingLines!, l => l.Proposed);
        Assert.Equal(baselineProposed.Label, resetProposed.Label);
        Assert.Equal(baselineProposed.Amount, resetProposed.Amount);
    }

    /// <summary>A Locked pricing snapshot refuses a save that changes an included-time override, like every other pricing-relevant input.</summary>
    [Fact]
    public async Task LockedPricing_RejectsIncludedTimeOverrideChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add("orders.lock_price");
        await CreateEngagedAgreementAsync(h, "Contract Override 4", includedLoadingMinutes: 30, extraHourlyRate: 75m);

        var created = await h.Sut.CreateAsync(ContractRequest(h.CustomerId, quantity: 3), CancellationToken.None);
        await h.Sut.SetOrderPricingStatusAsync(created.Order!.Id, OrderPricingStatus.Locked, CancellationToken.None);

        var update = BuildUpdateFrom(created.Order) with { IncludedLoadingMinutesOverride = 60 };
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpdateAsync(created.Order.Id, update, CancellationToken.None));
    }

    /// <summary>Changing an included-time override is audited on the Updated entry, with the old and new value both present.</summary>
    [Fact]
    public async Task IncludedTimeOverride_Change_IsAudited_WithOldAndNew()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            ContractRequest(h.CustomerId, quantity: 3) with { IncludedLoadingMinutesOverride = 30 },
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(30, created.Order!.IncludedLoadingMinutesOverride);

        var update = BuildUpdateFrom(created.Order) with { IncludedLoadingMinutesOverride = 60 };
        var updated = await h.Sut.UpdateAsync(created.Order.Id, update, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(60, updated.Order!.IncludedLoadingMinutesOverride);

        var audit = await h.Db.Context.AuditLogs
            .Where(a => a.EntityType == "TransportOrder" && a.Action == "Updated" && a.EntityId == created.Order.Id.ToString())
            .OrderByDescending(a => a.Id).FirstAsync();
        Assert.Contains("30", audit.OldValuesJson);
        Assert.Contains("60", audit.NewValuesJson);
    }
}
