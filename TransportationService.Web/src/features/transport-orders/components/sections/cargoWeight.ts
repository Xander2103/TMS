import { parseDecimalInput } from '../../../../utils/numbers'

/**
 * Weight arithmetic of a goods line (master sprint 2026-09-21, D4). Pure — no React, no I/O.
 *
 * Rules:
 *  - total = expected quantity × weight per unit, as long as the planner did not type the total;
 *  - a hand-typed total is MANUAL and is never overwritten ("Herbereken" returns it to auto);
 *  - a line with ONLY a total never yields a per-unit weight: 3 pallets / 6000 kg does not prove
 *    2000 kg each, so nothing is derived, displayed or written to the payload.
 * Different weights per unit are separate goods lines.
 */

/** The weight-relevant slice of a goods form row (`CargoFormRow` satisfies it). */
export interface CargoWeightFields {
  expectedQuantity: string
  weightPerUnitKg: string
  totalWeightKg: string
  /** True once the planner typed the total by hand; false = total follows quantity × weight per unit. */
  totalWeightIsManual: boolean
}

/** Kilograms are kept to the gram; also strips float noise (3 × 0.1). */
function roundKg(value: number): number {
  return Number(value.toFixed(3))
}

/** quantity × weight per unit, or null when either side is missing/invalid. */
export function computeAutoTotalWeightKg(expectedQuantity: string, weightPerUnitKg: string): number | null {
  const quantity = parseDecimalInput(expectedQuantity)
  const perUnit = parseDecimalInput(weightPerUnitKg)
  if (quantity === null || perUnit === null || quantity <= 0 || perUnit < 0) return null
  return roundKg(quantity * perUnit)
}

/**
 * Applies a field patch to a goods row and keeps the total weight consistent with the mode.
 * Typing the total makes it manual; changing quantity or weight per unit recomputes the total
 * only while it is still automatic (an auto total whose inputs disappear is cleared again —
 * it was never the planner's value).
 */
export function applyCargoWeightPatch<T extends CargoWeightFields>(row: T, patch: Partial<T>): T {
  const next: T = { ...row, ...patch }
  if (patch.totalWeightKg !== undefined && patch.totalWeightKg !== row.totalWeightKg) {
    next.totalWeightIsManual = true
    return next
  }
  const inputsChanged =
    (patch.expectedQuantity !== undefined && patch.expectedQuantity !== row.expectedQuantity) ||
    (patch.weightPerUnitKg !== undefined && patch.weightPerUnitKg !== row.weightPerUnitKg)
  if (!inputsChanged || next.totalWeightIsManual) return next
  const auto = computeAutoTotalWeightKg(next.expectedQuantity, next.weightPerUnitKg)
  next.totalWeightKg = auto === null ? '' : String(auto)
  return next
}

/**
 * "Herbereken": back to automatic. Without a computable total (no weight per unit) the row only
 * leaves manual mode when its total is empty — a lone total stays exactly as typed.
 */
export function recalculateTotalWeight<T extends CargoWeightFields>(row: T): T {
  const auto = computeAutoTotalWeightKg(row.expectedQuantity, row.weightPerUnitKg)
  if (auto === null) {
    return row.totalWeightKg.trim() === '' ? { ...row, totalWeightIsManual: false } : row
  }
  return { ...row, totalWeightKg: String(auto), totalWeightIsManual: false }
}

/** True when "Herbereken" would change the row (a computable total that differs from the shown one). */
export function canRecalculateTotalWeight(row: CargoWeightFields): boolean {
  const auto = computeAutoTotalWeightKg(row.expectedQuantity, row.weightPerUnitKg)
  return auto !== null && auto !== parseDecimalInput(row.totalWeightKg)
}

/** Total known, weight per unit not: the per-unit capacity cannot be judged (and is never guessed). */
export function isUnitWeightUnknown(row: CargoWeightFields): boolean {
  return parseDecimalInput(row.totalWeightKg) !== null && parseDecimalInput(row.weightPerUnitKg) === null
}

/**
 * Mode of a SAVED line (the API stores no flag): a total that equals quantity × weight per unit
 * was computed, anything else — including every historical total-only line — was typed.
 */
export function inferTotalWeightIsManual(
  expectedQuantity: number, weightPerUnitKg: number | null, totalWeightKg: number | null,
): boolean {
  if (totalWeightKg === null) return false
  if (weightPerUnitKg === null) return true
  return Math.abs(roundKg(expectedQuantity * weightPerUnitKg) - totalWeightKg) > 0.001
}

export interface CargoWeightTotals {
  /** Sum of the line totals; null when no line carries a total. */
  totalWeightKg: number | null
  /** Heaviest single unit — ONLY from lines that have a weight per unit; null when none has. */
  heaviestUnitKg: number | null
  /** Lines whose weight per unit is unknown (their heaviest unit cannot be judged). */
  linesWithoutUnitWeight: number
}

/** Aggregates for the capacity hint; works on saved lines (numbers) and form rows (parsed first). */
export function computeCargoWeightTotals(
  lines: Array<{ totalWeightKg: number | null; weightPerUnitKg: number | null }>,
): CargoWeightTotals {
  let total: number | null = null
  let heaviest: number | null = null
  let unknown = 0
  for (const line of lines) {
    if (line.totalWeightKg !== null) total = roundKg((total ?? 0) + line.totalWeightKg)
    if (line.weightPerUnitKg === null) unknown += 1
    else if (heaviest === null || line.weightPerUnitKg > heaviest) heaviest = line.weightPerUnitKg
  }
  return { totalWeightKg: total, heaviestUnitKg: heaviest, linesWithoutUnitWeight: unknown }
}

/** Form rows → the numeric shape `computeCargoWeightTotals` reads. */
export function cargoWeightTotalsFromRows(rows: CargoWeightFields[]): CargoWeightTotals {
  return computeCargoWeightTotals(
    rows.map((row) => ({
      totalWeightKg: parseDecimalInput(row.totalWeightKg),
      weightPerUnitKg: parseDecimalInput(row.weightPerUnitKg),
    })),
  )
}
