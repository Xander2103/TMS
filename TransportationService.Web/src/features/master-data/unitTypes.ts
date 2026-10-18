import type { UnitTypeLabel } from './api/unitTypesApi'

/** The `{id, code, name}` shape every unit picker / label resolver consumes. */
export interface UnitChoice {
  id: string
  code: string
  name: string
}

type Selected = ReadonlyArray<string | null | undefined>

/**
 * Units split by purpose (master-data remediation 2026-10-18): pickers offer only what the
 * backend accepts (order-options, resp. active pricing units), while every DISPLAY resolves
 * through the labels of all codes ever used — so a historical "TMKIST" keeps its human name.
 */
export interface UnitTypesModel {
  /** Pickable for order entry (real unit-type ids — the price preview maps code → id with them). */
  orderOptions: UnitChoice[]
  labels: UnitTypeLabel[]
  /** Every known unit as `{id, code, name}`, for name lookups (`unitLabelFrom`, summaries). */
  displayUnits: UnitChoice[]
  /** Human name of a unit code; the code itself only when the code is truly unknown. */
  labelOf: (code: string | null | undefined) => string
  /** Order-entry options, plus each selected code that is no longer pickable ("<naam> (inactief)"). */
  orderPickerOptions: (selected: Selected) => UnitChoice[]
  /** Active pricing units, plus each selected code that is no longer pickable. */
  pricingPickerOptions: (selected: Selected) => UnitChoice[]
}

/** Builds the "<naam> (inactief)" text of a retained, no-longer-pickable value. */
export type RetainedLabel = (name: string, label: UnitTypeLabel | undefined) => string

const keyOf = (code: string) => code.trim().toUpperCase()

export function buildUnitTypesModel(
  orderOptions: UnitChoice[],
  labels: UnitTypeLabel[],
  retainedLabel: RetainedLabel,
): UnitTypesModel {
  const labelByCode = new Map(labels.map((l) => [keyOf(l.code), l]))
  const optionByCode = new Map(orderOptions.map((o) => [keyOf(o.code), o]))
  const nameOf = (code: string) =>
    labelByCode.get(keyOf(code))?.name ?? optionByCode.get(keyOf(code))?.name ?? code
  const choiceOf = (l: UnitTypeLabel): UnitChoice => ({
    id: optionByCode.get(keyOf(l.code))?.id ?? `unit:${l.code}`,
    code: l.code,
    name: l.name,
  })

  const displayUnits: UnitChoice[] = [
    ...labels.map(choiceOf),
    // An option the labels endpoint did not (yet) return still resolves to its name.
    ...orderOptions.filter((o) => !labelByCode.has(keyOf(o.code))),
  ]

  const withRetained = (base: UnitChoice[], selected: Selected): UnitChoice[] => {
    const present = new Set(base.map((u) => keyOf(u.code)))
    const retained = [...new Set(selected.filter((c): c is string => typeof c === 'string' && c.trim() !== ''))]
      .filter((code) => !present.has(keyOf(code)))
      .map((code) => ({
        // Never a real id: these codes must not feed a code → id mapping (e.g. the price preview).
        id: `retained:${code}`,
        code,
        name: retainedLabel(nameOf(code), labelByCode.get(keyOf(code))),
      }))
    return [...base, ...retained]
  }

  const pricingOptions = labels.filter((l) => l.isActive && l.allowForPricing && !l.isDeleted).map(choiceOf)

  return {
    orderOptions,
    labels,
    displayUnits,
    labelOf: (code) => (code ? nameOf(code) : ''),
    orderPickerOptions: (selected) => withRetained(orderOptions, selected),
    pricingPickerOptions: (selected) => withRetained(pricingOptions, selected),
  }
}
