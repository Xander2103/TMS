using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// WH-H1 (warehouse acceptance 2026-10-18): a receipt of 2,147,483,647 on a stock of 25 answered
/// 200 and wrapped the cached stock to −2,147,483,624. The QUANTITY DOMAIN of company items
/// ("bedrijfsmiddelen") is deliberately <b>whole units</b>: items are issued per piece, pair, set,
/// box, roll — and also per meter / liter / kilogram as a whole count (a 1 l bottle, a 5 m roll).
/// Fractions are not supported by the data model (int columns) and are refused with a Dutch message
/// instead of a raw .NET conversion error.
/// <para>
/// Every stock-changing command validates its quantity against these bounds BEFORE mutating, and
/// <see cref="Apply"/> (the single arithmetic point of the ledger) uses checked arithmetic plus the
/// stock bounds as the last line of defence — the cache can never wrap.
/// </para>
/// </summary>
public static class StockQuantities
{
    /// <summary>Largest quantity one movement (receipt, issue, return, correction delta) may carry.</summary>
    public const int MaxMovement = 100_000;

    /// <summary>Largest absolute stock level a template/variant may hold (positive or, when allowed, negative).</summary>
    public const int MaxStock = 1_000_000;

    private const string MaxMovementText = "100.000";
    private const string MaxStockText = "1.000.000";

    public static readonly string WholeUnitsHint = "Hoeveelheden zijn altijd gehele aantallen (geen decimalen).";

    /// <summary>Receipt / issue / return quantity: a whole number between 1 and <see cref="MaxMovement"/>.</summary>
    public static void EnsureMovementQuantity(string field, int quantity, string what = "De hoeveelheid")
    {
        if (quantity < 1)
        {
            throw new DomainValidationException(field, $"{what} moet minstens 1 zijn.");
        }

        if (quantity > MaxMovement)
        {
            throw new DomainValidationException(field,
                $"{what} is te groot: maximaal {MaxMovementText} per registratie.");
        }
    }

    /// <summary>A target stock level (correction / initial stock / template "Voorraad").</summary>
    public static void EnsureStockLevel(string field, int level)
    {
        if (level > MaxStock || level < -MaxStock)
        {
            throw new DomainValidationException(field,
                $"Deze voorraad is te groot: de voorraad moet tussen -{MaxStockText} en {MaxStockText} liggen.");
        }
    }

    /// <summary>A threshold / target / pack size: between 0 and <see cref="MaxStock"/>.</summary>
    public static void EnsureThreshold(string field, int? value)
    {
        if (value is > MaxStock)
        {
            throw new DomainValidationException(field, $"Deze waarde is te groot (maximaal {MaxStockText}).");
        }
    }

    /// <summary>
    /// current + delta with overflow and bound checks; throws a Dutch validation error instead of
    /// wrapping. The ledger calls this BEFORE assigning the cached stock.
    /// </summary>
    public static int Apply(int current, int delta)
    {
        // A legacy row already outside the bounds (the wrapped acceptance stock) can only be
        // corrected back INTO them; every other mutation on it is refused.
        long result = (long)current + delta;
        if (result > MaxStock || result < -MaxStock)
        {
            throw new DomainValidationException("quantity",
                $"Deze mutatie brengt de voorraad buiten de toegelaten grenzen (-{MaxStockText} tot {MaxStockText}). De voorraad is niet gewijzigd.");
        }

        return checked(current + delta);
    }
}
