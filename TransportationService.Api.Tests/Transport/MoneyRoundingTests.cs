using TransportationService.Api.Common.Money;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using Xunit;

namespace TransportationService.Api.Tests.Transport;

/// <summary>T-H4: one rounding rule from sales line to invoice VAT total (half away from zero, exact decimal).</summary>
public class MoneyRoundingTests
{
    [Theory]
    [InlineData(2.5, 125.33, 313.33)]
    [InlineData(1.5, 0.01, 0.02)]
    [InlineData(-2.5, 0.01, -0.03)]
    [InlineData(12, 650, 7800)]
    public void LineTotal_IsExactAndRoundsHalfAwayFromZero(double q, double p, double expected) =>
        Assert.Equal((decimal)expected, MoneyRounding.LineTotal((decimal)q, (decimal)p));

    [Fact]
    public void InvoiceVat_UsesTheSameLineTotalsAsTheSalesLine()
    {
        // Base 313.33 (not 313.32) → 21 % VAT = 65.7993 → 65.80.
        var line = new InvoiceLine { Quantity = 2.5m, UnitPrice = 125.33m, VatRatePercent = 21m };
        Assert.Equal(65.80m, InvoiceTotals.VatTotal([line], null));
    }
}
