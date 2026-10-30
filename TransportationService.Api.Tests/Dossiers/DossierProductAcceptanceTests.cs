using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Product acceptance sprint 2026-10-07 — root causes found while walking the acceptance list:
/// a cancelled dossier was editable through the API (only Closed was guarded), reopen and cancel
/// had no compare-and-set (two reopen requests = two audits), and "CMR missing" was reported for
/// executed on-site lifting work that never gets a CMR. Plus the explicit historical-dossier flow.
/// </summary>
public class DossierProductAcceptanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 07, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        public DossierLifecycleService Lifecycle(TransportationService.Api.Data.TransportationDbContext? context = null, Guid? userId = null)
        {
            var ctx = context ?? Db.Context;
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId ?? UserId);
            var audit = new AuditService(ctx, tenant, user);
            var dossiers = new DossierService(ctx, tenant, audit, new TestClock(Now));
            return new DossierLifecycleService(ctx, tenant, audit, new TestClock(Now), user, () => dossiers, new DossierReadinessService(ctx, tenant));
        }

        public DossierService Dossiers(TransportationService.Api.Data.TransportationDbContext? context = null)
        {
            var ctx = context ?? Db.Context;
            return new DossierService(ctx, new DevTenantContext(TenantId), new AuditService(ctx, new DevTenantContext(TenantId), new DevCurrentUserContext(UserId)), new TestClock(Now));
        }

        public DossierNoteService Notes() =>
            new(Db.Context, new DevTenantContext(TenantId), new AuditService(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(UserId)), new DevCurrentUserContext(UserId));

        public Task<TransportDossier> Entity(Guid id) => Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == id);

        public Task<List<Modules.Auditing.Entities.AuditLog>> Audits(Guid id, string action) =>
            Db.Context.AuditLogs.AsNoTracking().Where(a => a.EntityType == "TransportDossier" && a.EntityId == id.ToString() && a.Action == action).ToListAsync();

        public async Task<Guid> TypeIdAsync(string code) => (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        /// <summary>Dossier + one transport activity/order per status (loading + unloading stop each).</summary>
        public async Task<(Guid DossierId, List<Guid> OrderIds)> DossierWithOrdersAsync(DateOnly? dossierDate, params TransportOrderStatus[] statuses)
        {
            var dossier = await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId, DossierDate: dossierDate), CancellationToken.None);
            var typeId = await TypeIdAsync("DIRECT_TRANSPORT");
            var orderIds = new List<Guid>();
            var sequence = 1;
            foreach (var status in statuses)
            {
                var orderId = Guid.NewGuid();
                Db.Context.TransportOrders.Add(new TransportOrder { Id = orderId, TenantId = TenantId, CustomerId = CustomerId, OrderNumber = $"ORD-{sequence}", OrderDate = new(2026, 10, 6), Status = status, GoodsDescription = "Paletten" });
                Db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = TenantId, DossierId = dossier.Id, TransportOrderId = orderId });
                Db.Context.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = TenantId, DossierId = dossier.Id, ActivityTypeId = typeId, Sequence = sequence, LinkedTransportOrderId = orderId });
                Db.Context.TransportOrderStops.AddRange(
                    new TransportOrderStop { Id = Guid.NewGuid(), TenantId = TenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                    new TransportOrderStop { Id = Guid.NewGuid(), TenantId = TenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" });
                orderIds.Add(orderId);
                sequence++;
            }

            await Db.Context.SaveChangesAsync();
            return (dossier.Id, orderIds);
        }

        /// <summary>Dossier with one completed on-site lifting job (crane work, a site stop, no goods).</summary>
        public async Task<Guid> DossierWithCompletedOnSiteLiftingAsync()
        {
            var dossier = await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId), CancellationToken.None);
            var typeId = await TypeIdAsync("KRAANTRANSPORT");
            var orderId = Guid.NewGuid();
            Db.Context.TransportOrders.Add(new TransportOrder
            {
                Id = orderId, TenantId = TenantId, CustomerId = CustomerId, OrderNumber = "ORD-KRAAN", OrderDate = new(2026, 10, 6),
                Status = TransportOrderStatus.Completed, CraneJobKind = CraneJobKind.OnSiteLifting, GoodsDescription = "Hijswerk",
            });
            Db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = TenantId, DossierId = dossier.Id, TransportOrderId = orderId });
            Db.Context.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = TenantId, DossierId = dossier.Id, ActivityTypeId = typeId, Sequence = 1, LinkedTransportOrderId = orderId });
            Db.Context.TransportOrderStops.Add(new TransportOrderStop { Id = Guid.NewGuid(), TenantId = TenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Site, City = "Mechelen" });
            await Db.Context.SaveChangesAsync();
            return dossier.Id;
        }
    }

    /// <summary>Runs a competitor right before the first UPDATE on transport_dossiers (see the integrity tests).</summary>
    private sealed class CompetingDossierUpdateInterceptor : DbCommandInterceptor
    {
        public Func<Task>? Competitor { get; set; }
        public int Fired { get; private set; }

        private async Task RaceAsync(DbCommand command)
        {
            if (Competitor is { } competitor && Fired == 0 && command.CommandText.Contains("transport_dossiers", StringComparison.Ordinal)
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                Fired++;
                await competitor();
            }
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RaceAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await RaceAsync(command);
            return result;
        }
    }

    private static async Task<Harness> SeedAsync(params IInterceptor[] extraInterceptors)
    {
        var db = new SqliteTestDbContext(null, extraInterceptors);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR", TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
        });
        var entityId = Guid.NewGuid();
        db.Context.LegalEntities.Add(new LegalEntity { Id = entityId, TenantId = tenantId, LegalName = "Acme BV", IsActive = true, IsDefault = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV", VatNumber = "BE0123456789", IsActive = true, DefaultLegalEntityId = entityId });
        db.Context.Users.Add(new User { Id = UserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId);
    }

    // ------------------------------------------------------------ 1. cancelled = read-only, server-side

    [Fact]
    public async Task CancelledDossier_IsReadOnlyThroughTheApi_UntilReopened()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, _) = await h.DossierWithOrdersAsync(null, TransportOrderStatus.Draft);
        await h.Lifecycle().CancelAsync(dossierId, new CancelDossierRequest("Klant zegt af"), CancellationToken.None);
        Assert.Equal(DossierStatus.Cancelled, (await h.Entity(dossierId)).Status);

        // Every write path refuses and names the real state — not "gesloten", not a silent 200.
        var edit = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Dossiers().UpdateAsync(dossierId, new SaveDossierRequest(Title: "Toch bewerkt", CustomerId: h.CustomerId), CancellationToken.None));
        Assert.Contains("geannuleerd", edit.Message);
        var note = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Notes().CreateAsync(dossierId, new CreateDossierNoteRequest("Nog een notitie"), CancellationToken.None));
        Assert.Contains("geannuleerd", note.Message);
        Assert.NotEqual("Toch bewerkt", (await h.Entity(dossierId)).Title);

        // Reopening (reason required) makes it editable again.
        await h.Lifecycle().ReopenAsync(dossierId, new ReopenDossierRequest("Toch uitvoeren"), CancellationToken.None);
        var updated = await h.Dossiers().UpdateAsync(dossierId, new SaveDossierRequest(Title: "Toch bewerkt", CustomerId: h.CustomerId), CancellationToken.None);
        Assert.Equal("Toch bewerkt", updated!.Title);
    }

    [Fact]
    public void OnlyAnOpenDossier_IsEditable()
    {
        Assert.True(DossierStateRules.IsEditable(DossierStatus.Open));
        Assert.False(DossierStateRules.IsEditable(DossierStatus.Closed));
        Assert.False(DossierStateRules.IsEditable(DossierStatus.Cancelled));
        Assert.Contains("bevestigd", DossierStateRules.ReadOnlyMessage(DossierStatus.Closed));
        Assert.Contains("geannuleerd", DossierStateRules.ReadOnlyMessage(DossierStatus.Cancelled));
    }

    // ------------------------------------------------------------ 2. reopen / cancel races

    [Fact]
    public async Task TwoNearlySimultaneousReopens_ReopenExactlyOnce_WithOneAudit()
    {
        var interceptor = new CompetingDossierUpdateInterceptor();
        var h = await SeedAsync(interceptor);
        using var _ = h.Db;
        var (dossierId, _) = await h.DossierWithOrdersAsync(null, TransportOrderStatus.Completed);
        await h.Lifecycle().ConfirmAsync(dossierId, new ConfirmDossierRequest(AcknowledgeWarnings: true), CancellationToken.None);
        Assert.Equal(DossierStatus.Closed, (await h.Entity(dossierId)).Status);

        // Request 1 reopens through a second context right before request 2's UPDATE lands.
        await using var other = h.Db.CreateContextForTenant(h.TenantId);
        var otherUser = Guid.NewGuid();
        h.Db.Context.Users.Add(new User { Id = otherUser, TenantId = h.TenantId, Email = "collega@acme.be", FirstName = "An", LastName = "Collega", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        DossierDetailDto? request1Result = null;
        interceptor.Competitor = async () => request1Result = await h.Lifecycle(other, otherUser).ReopenAsync(dossierId, new ReopenDossierRequest("Collega heropent"), CancellationToken.None);

        var loser = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().ReopenAsync(dossierId, new ReopenDossierRequest("Ik heropen ook"), CancellationToken.None));

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal("Open", request1Result!.Status);
        Assert.Contains("al open", loser.Message);
        var entity = await h.Entity(dossierId);
        Assert.Equal((DossierStatus.Open, (DossierConfirmationSource?)null, (DateTime?)null), (entity.Status, entity.ConfirmationSource, entity.ClosedAt));
        var audit = Assert.Single(await h.Audits(dossierId, "Reopened"));
        Assert.Contains("Collega heropent", audit.NewValuesJson);
        Assert.DoesNotContain("Ik heropen ook", audit.NewValuesJson);
    }

    [Fact]
    public async Task CancelThatLosesAgainstAConfirmation_ChangesNothing_AndAuditsNothing()
    {
        var interceptor = new CompetingDossierUpdateInterceptor();
        var h = await SeedAsync(interceptor);
        using var _ = h.Db;
        var (dossierId, orderIds) = await h.DossierWithOrdersAsync(null, TransportOrderStatus.Draft);

        // A colleague confirms (historic dossier: warnings acknowledged) a moment before the cancel UPDATE.
        await using var other = h.Db.CreateContextForTenant(h.TenantId);
        interceptor.Competitor = async () => await h.Lifecycle(other).ConfirmAsync(dossierId, new ConfirmDossierRequest("Al uitgevoerd", AcknowledgeWarnings: true), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().CancelAsync(dossierId, new CancelDossierRequest("Klant zegt af"), CancellationToken.None));

        Assert.Equal(1, interceptor.Fired);
        Assert.Contains("afgesloten", refused.Message);
        var entity = await h.Entity(dossierId);
        Assert.Equal((DossierStatus.Closed, DossierConfirmationSource.Manual, (DateTime?)null), (entity.Status, entity.ConfirmationSource, entity.CancelledAt));
        // The draft order was NOT cascaded by the losing cancel.
        Assert.Equal(TransportOrderStatus.Draft, h.Db.Context.TransportOrders.AsNoTracking().Single(o => o.Id == orderIds[0]).Status);
        Assert.Empty(await h.Audits(dossierId, "Cancelled"));
        Assert.Single(await h.Audits(dossierId, "ConfirmedManually"));
    }

    // ------------------------------------------------------------ 3. CMR is a capability rule

    [Fact]
    public async Task OnSiteLiftingOnly_IsNeverToldItsCmrIsMissing_AGoodsTransportIs()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var crane = await h.DossierWithCompletedOnSiteLiftingAsync();
        var (goods, _) = await h.DossierWithOrdersAsync(null, TransportOrderStatus.Completed);

        var craneEvaluation = (await h.Lifecycle().EvaluateAsync(crane, CancellationToken.None))!;
        var goodsEvaluation = (await h.Lifecycle().EvaluateAsync(goods, CancellationToken.None))!;

        Assert.DoesNotContain(craneEvaluation.Warnings, c => c.Code == "documents.cmr_missing");
        Assert.True(craneEvaluation.CanAutoConfirm);
        var cmr = Assert.Single(goodsEvaluation.Warnings, c => c.Code == "documents.cmr_missing");
        Assert.Equal("Info", cmr.Severity);
    }

    // ------------------------------------------------------------ 4. historical dossier (spec §6 / §30 C)

    [Fact]
    public async Task HistoricDossier_RegisteredAfterTheTransport_ConfirmsWithoutAFakeOperationalFlow_AndSurvivesReload()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var yesterday = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1);
        var (dossierId, _) = await h.DossierWithOrdersAsync(yesterday, TransportOrderStatus.Draft);

        // No trip, no dispatch, no stop completion: everything operational is a WARNING, nothing blocks.
        var evaluation = (await h.Lifecycle().EvaluateAsync(dossierId, CancellationToken.None))!;
        Assert.True(evaluation.CanConfirmManually);
        Assert.False(evaluation.CanAutoConfirm);
        Assert.Empty(evaluation.Blockers);
        Assert.Contains(evaluation.Warnings, c => c.Code == "order.not_completed" && c.Severity == "Warning");
        Assert.Contains(evaluation.Warnings, c => c.Code == "nothing.completed" && c.Severity == "Hold");

        // Deliberate acknowledgement is required…
        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Lifecycle().ConfirmAsync(dossierId, new ConfirmDossierRequest("Transport gisteren uitgevoerd"), CancellationToken.None));
        Assert.Contains("aandachtspunten", refused.Message);
        Assert.Equal(DossierStatus.Open, (await h.Entity(dossierId)).Status);

        // …and then the confirmation is manual, attributed, reasoned, audited once.
        var confirmed = await h.Lifecycle().ConfirmAsync(dossierId, new ConfirmDossierRequest("Transport gisteren uitgevoerd", AcknowledgeWarnings: true), CancellationToken.None);
        Assert.Equal(("Closed", "Manual", UserId, "Transport gisteren uitgevoerd"), (confirmed!.Status, confirmed.ConfirmationSource, confirmed.ConfirmedByUserId, confirmed.ConfirmationReason));
        Assert.Equal(Now.UtcDateTime, confirmed.ConfirmedAt);

        // Reload through a fresh context (an F5): identical metadata, no status fallback, no extra audit.
        await using var fresh = h.Db.CreateContextForTenant(h.TenantId);
        var reloaded = (await h.Dossiers(fresh).GetAsync(dossierId, CancellationToken.None))!;
        Assert.Equal((confirmed.Status, confirmed.ConfirmationSource, confirmed.ConfirmedByUserId, confirmed.ConfirmedAt, confirmed.ConfirmationReason),
            (reloaded.Status, reloaded.ConfirmationSource, reloaded.ConfirmedByUserId, reloaded.ConfirmedAt, reloaded.ConfirmationReason));
        Assert.Single(await h.Audits(dossierId, "ConfirmedManually"));
        // The Draft order was never pushed through a fictitious workflow.
        Assert.All(reloaded.Activities!, a => Assert.Equal("Draft", a.LinkedOrderStatus));
    }
}
