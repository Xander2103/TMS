import { formatQuantity } from '../../../utils/numbers'
import type { UnitOptionItem } from '../components/UnitSelect'
import type { CargoItem } from '../types'

/** Resolves a goods unit for display (same contract as the order shell's `unitLabel`). */
export type UnitLabelResolver = (code: string | null, legacy: string | null) => string

/** Managed unit-type name when a code is set, else the preserved legacy free-text value. */
export function unitLabelFrom(units: UnitOptionItem[]): UnitLabelResolver {
  return (code, legacy) => (code ? (units.find((u) => u.code === code)?.name ?? code) : null) ?? legacy ?? ''
}

/** "Europallet · 2 Europallet · 480 kg" — description · quantity + unit · weight (each part only when known). */
export function describeCargoItem(item: CargoItem, unitLabel: UnitLabelResolver): string {
  const quantity = `${formatQuantity(item.expectedQuantity)} ${unitLabel(item.quantityUnitCode, item.quantityUnit)}`.trim()
  const parts = [item.description?.trim() || null, quantity, item.totalWeightKg != null ? `${formatQuantity(item.totalWeightKg)} kg` : null]
  return parts.filter(Boolean).join(' · ')
}
