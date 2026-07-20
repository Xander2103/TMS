using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

public class RateCardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        public RateCardService Sut(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new RateCardService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid())));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, customerId);
    }

    private static SaveRateCardRequest ValidCard(Guid customerId) => new(
        customerId, "Standaard 2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
        BaseAmount: 50m, PerKmRate: 1.20m, PerPalletRate: 5m, MinimumAmount: 100m,
        Surcharges: [new SaveRateSurchargeRequest("Diesel", "Percent", 10m), new SaveRateSurchargeRequest("ADR", "Fixed", 25m)]);

    [Fact]
    public async Task Quote_BuildsExplanationLines_WithSurchargesAndRounding()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(ValidCard(h.CustomerId), CancellationToken.None);

        var quote = await sut.QuoteAsync(
            new QuoteRequest(h.CustomerId, new DateOnly(2026, 6, 15), DistanceKm: 100, PalletCount: 4),
            CancellationToken.None);

        // 50 base + 120 km + 20 pallets = 190 subtotal; +10% diesel (19) + 25 ADR = 234.
        Assert.Equal(234m, quote.Total);
        Assert.Equal("Standaard 2026", quote.RateCardName);
        Assert.Contains(quote.Lines, l => l.Label == "Basisbedrag" && l.Amount == 50m);
        Assert.Contains(quote.Lines, l => l.Label.StartsWith("Afstand") && l.Amount == 120m);
        Assert.Contains(quote.Lines, l => l.Label.StartsWith("Pallets") && l.Amount == 20m);
        Assert.Contains(quote.Lines, l => l.Label.Contains("Diesel") && l.Amount == 19m);
        Assert.Contains(quote.Lines, l => l.Label.Contains("ADR") && l.Amount == 25m);
    }

    [Fact]
    public async Task Quote_AppliesMinimum_AndExplainsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(ValidCard(h.CustomerId), CancellationToken.None);

        // Only the base (50) + 10% (5) + 25 = 80 → below the 100 minimum.
        var quote = await sut.QuoteAsync(
            new QuoteRequest(h.CustomerId, new DateOnly(2026, 6, 15)), CancellationToken.None);

        Assert.Equal(100m, quote.Total);
        Assert.Contains(quote.Lines, l => l.Label.StartsWith("Minimumtarief"));
    }

    [Fact]
    public async Task Quote_RequiresAnEffectiveCard()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(ValidCard(h.CustomerId), CancellationToken.None);

        // Outside the window → no card.
        var outside = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.QuoteAsync(new QuoteRequest(h.CustomerId, new DateOnly(2027, 1, 1)), CancellationToken.None));
        Assert.Contains("customerId", outside.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_RefusesOverlappingWindows_ForTheSameCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(ValidCard(h.CustomerId), CancellationToken.None);

        var overlap = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidCard(h.CustomerId) with
            {
                Name = "Zomer",
                EffectiveFrom = new DateOnly(2026, 6, 1),
                EffectiveUntil = null,
            }, CancellationToken.None));
        Assert.Contains("effectiveFrom", overlap.FieldErrors!.Keys);

        // Adjacent (non-overlapping) window is fine.
        var next = await sut.CreateAsync(ValidCard(h.CustomerId) with
        {
            Name = "2027",
            EffectiveFrom = new DateOnly(2027, 1, 1),
            EffectiveUntil = null,
        }, CancellationToken.None);
        Assert.Equal("2027", next.Name);
    }

    [Fact]
    public async Task Create_ValidatesCustomerDatesAndSurcharges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var badCustomer = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidCard(Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("customerId", badCustomer.FieldErrors!.Keys);

        var badDates = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidCard(h.CustomerId) with
            {
                EffectiveFrom = new DateOnly(2026, 12, 1),
                EffectiveUntil = new DateOnly(2026, 1, 1),
            }, CancellationToken.None));
        Assert.Contains("effectiveUntil", badDates.FieldErrors!.Keys);

        var badSurcharge = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(ValidCard(h.CustomerId) with
            {
                Surcharges = [new SaveRateSurchargeRequest("X", "Nonsense", 1m)],
            }, CancellationToken.None));
        Assert.Contains("surcharges", badSurcharge.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Update_ReplacesSurcharges_AndListIsTenantScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var card = await sut.CreateAsync(ValidCard(h.CustomerId), CancellationToken.None);

        var updated = await sut.UpdateAsync(card.Id, ValidCard(h.CustomerId) with
        {
            Surcharges = [new SaveRateSurchargeRequest("Tol", "Fixed", 12.5m)],
        }, CancellationToken.None);
        var surcharge = Assert.Single(updated!.Surcharges);
        Assert.Equal("Tol", surcharge.Name);

        Assert.Single(await sut.ListAsync(h.CustomerId, CancellationToken.None));
        Assert.Empty(await h.Sut(tenantId: Guid.NewGuid()).ListAsync(null, CancellationToken.None));
    }
}
