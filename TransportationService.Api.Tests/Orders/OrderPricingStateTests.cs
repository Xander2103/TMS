using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// UX sprint 2026-09-09 §2.5, hardened 2026-09-10 — ONE definition of "priced" that follows
/// pricing provenance: € 0 is a legitimate price when it was explicitly agreed (override or
/// one-off agreement); an engine-generated empty zero is not.
/// </summary>
public class OrderPricingStateTests
{
    public static TheoryData<bool, OrderPricingSource, double?, double?, bool> Matrix => new()
    {
        // priceIsManual, source, oneOffFixedAmount, agreedPrice, expected
        { false, OrderPricingSource.Contract, null, null, false },   // nothing derived or entered
        { false, OrderPricingSource.Contract, null, 0.0, false },    // engine wrote 0: no counted line
        { false, OrderPricingSource.Contract, null, 0.01, true },
        { false, OrderPricingSource.Contract, null, 450.0, true },
        { true, OrderPricingSource.Contract, null, null, true },     // override always counts
        { true, OrderPricingSource.Contract, null, 0.0, true },      // deliberate € 0 with a reason
        { true, OrderPricingSource.Contract, null, 450.0, true },
        { false, OrderPricingSource.OneOff, 0.0, 0.0, true },        // one-off agreement at € 0 is a price
        { false, OrderPricingSource.OneOff, 250.0, 250.0, true },
        { false, OrderPricingSource.OneOff, 250.0, null, true },     // agreed, engine not yet run
        { false, OrderPricingSource.OneOff, null, 0.0, false },      // invalid state guarded by validation; never priced by accident
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void IsPriced_FollowsPricingProvenance(
        bool priceIsManual, OrderPricingSource source, double? oneOff, double? agreedPrice, bool expected)
    {
        decimal? fixedAmount = oneOff is { } f ? (decimal)f : null;
        decimal? agreed = agreedPrice is { } a ? (decimal)a : null;
        var order = new TransportOrder { PriceIsManual = priceIsManual, PricingSource = source, OneOffFixedAmount = fixedAmount, AgreedPrice = agreed };

        Assert.Equal(expected, OrderPricingState.IsPriced(order));
        Assert.Equal(expected, OrderPricingState.IsPriced(priceIsManual, source, fixedAmount, agreed));
        Assert.Equal(expected, OrderPricingState.IsPricedExpression.Compile()(order));
    }

    /// <summary>
    /// The expression must translate to SQL both at the top level and nested inside a projection
    /// (the dossier list counts priced orders per row), and SQL NULL semantics must match the C#
    /// definition for every row of the matrix.
    /// </summary>
    [Fact]
    public async Task IsPricedExpression_TranslatesInSql_AndAgreesWithTheCompiledDefinition()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        var expectedById = new Dictionary<Guid, bool>();
        var sequence = 0;
        foreach (var row in Matrix)
        {
            var (priceIsManual, source, oneOff, agreedPrice, expected) = ((bool)row[0], (OrderPricingSource)row[1], (double?)row[2], (double?)row[3], (bool)row[4]);
            var order = new TransportOrder
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = $"ORD-{++sequence:0000}",
                OrderDate = new DateOnly(2026, 9, 10),
                PriceIsManual = priceIsManual, PricingSource = source,
                OneOffFixedAmount = oneOff is { } f ? (decimal)f : null,
                AgreedPrice = agreedPrice is { } a ? (decimal)a : null,
            };
            db.Context.TransportOrders.Add(order);
            expectedById[order.Id] = expected;
        }
        await db.Context.SaveChangesAsync();

        var pricedIds = await db.Context.TransportOrders.AsNoTracking()
            .Where(OrderPricingState.IsPricedExpression)
            .Select(o => o.Id)
            .ToListAsync();
        Assert.Equal(expectedById.Where(kv => kv.Value).Select(kv => kv.Key).Order(), pricedIds.Order());

        var pricedCount = await db.Context.TransportOrders.AsNoTracking().CountAsync(OrderPricingState.IsPricedExpression);
        Assert.Equal(expectedById.Count(kv => kv.Value), pricedCount);

        // The negation must treat SQL NULL like C# (null AgreedPrice → unpriced), not as "unknown".
        var unpricedIds = await db.Context.TransportOrders.AsNoTracking()
            .Where(OrderPricingState.IsUnpricedExpression)
            .Select(o => o.Id)
            .ToListAsync();
        Assert.Equal(expectedById.Where(kv => !kv.Value).Select(kv => kv.Key).Order(), unpricedIds.Order());

        // Nested in a projection, as the dossier list does per dossier row.
        var nested = await db.Context.Customers.AsNoTracking()
            .Select(c => new
            {
                c.Id,
                Priced = db.Context.TransportOrders.Where(o => o.CustomerId == c.Id).Where(OrderPricingState.IsPricedExpression).Count(),
                Unpriced = db.Context.TransportOrders.Where(o => o.CustomerId == c.Id).Where(OrderPricingState.IsUnpricedExpression).Count(),
            })
            .SingleAsync();
        Assert.Equal(expectedById.Count(kv => kv.Value), nested.Priced);
        Assert.Equal(expectedById.Count(kv => !kv.Value), nested.Unpriced);
    }
}
