using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// Single source of truth for the stock status rules (docs/features/
/// inventory-tasks-notifications-sprint.md §4):
///   NegativeStock  stock &lt; 0
///   OutOfStock     stock == 0
///   CriticalStock  0 &lt; stock ≤ minimum
///   LowStock       minimum &lt; stock ≤ warning (minimum treated as 0 when unset)
///   Normal         everything else
/// A null threshold simply removes its band; a variant's own warning level wins over the
/// template's, the minimum level always comes from the template.
/// </summary>
public static class InventoryStatusCalculator
{
    public static InventoryStatus Compute(int stock, int? warningLevel, int? minimumLevel)
    {
        if (stock < 0)
        {
            return InventoryStatus.NegativeStock;
        }

        if (stock == 0)
        {
            return InventoryStatus.OutOfStock;
        }

        if (minimumLevel is { } minimum && stock <= minimum)
        {
            return InventoryStatus.CriticalStock;
        }

        if (warningLevel is { } warning && stock <= warning)
        {
            return InventoryStatus.LowStock;
        }

        return InventoryStatus.Normal;
    }

    public static InventoryStatus ComputeFor(IssuedItemTemplate template, IssuedItemVariant? variant) =>
        Compute(
            variant?.CurrentStock ?? template.CurrentStock,
            variant?.LowStockThreshold ?? template.LowStockThreshold,
            template.MinimumStock);
}
