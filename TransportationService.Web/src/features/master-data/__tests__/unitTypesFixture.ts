import type { UnitTypeLabel } from '../api/unitTypesApi'
import type { UseUnitTypesResult } from '../hooks/useUnitTypes'
import { buildUnitTypesModel, type UnitChoice } from '../unitTypes'

/**
 * Test stand-in for `useUnitTypes()`: `options` are the order-entry units; `labels` default to
 * one active, order- and pricing-enabled label per option (pass extra labels for retired units).
 * Use from a `vi.mock` factory via a dynamic import (the factory is hoisted).
 */
export function unitTypesFixture(options: UnitChoice[], labels?: UnitTypeLabel[]): UseUnitTypesResult {
  const allLabels =
    labels ??
    options.map((o) => ({
      code: o.code,
      name: o.name,
      isActive: true,
      allowForOrderEntry: true,
      allowForPricing: true,
      isDeleted: false,
    }))
  return {
    ...buildUnitTypesModel(options, allLabels, (name, label) =>
      !label || !label.isActive || label.isDeleted ? `${name} (inactief)` : `${name} (niet meer selecteerbaar)`,
    ),
    isLoading: false,
  }
}
