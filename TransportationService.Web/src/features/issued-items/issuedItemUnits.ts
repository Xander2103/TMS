import type { TranslateFn } from '../../i18n/localeContext'

/**
 * Fixed unit catalogue for issued-item templates — the frontend half of ONE shared catalogue.
 * The backend accepts exactly these codes (anything else is a 400 on `unit`) and stores the
 * code, never a display string; the label is a translation key so every language renders it.
 */
export const ISSUED_ITEM_UNITS = ['piece', 'pair', 'set', 'box', 'pack', 'roll', 'meter', 'liter', 'kilogram', 'other'] as const

export type IssuedItemUnit = (typeof ISSUED_ITEM_UNITS)[number]

export const DEFAULT_ISSUED_ITEM_UNIT: IssuedItemUnit = 'piece'

/** Vertaalsleutels — renderen als t(ISSUED_ITEM_UNIT_LABELS[unit]). */
export const ISSUED_ITEM_UNIT_LABELS: Record<IssuedItemUnit, string> = {
  piece: 'issuedItems.units.piece',
  pair: 'issuedItems.units.pair',
  set: 'issuedItems.units.set',
  box: 'issuedItems.units.box',
  pack: 'issuedItems.units.pack',
  roll: 'issuedItems.units.roll',
  meter: 'issuedItems.units.meter',
  liter: 'issuedItems.units.liter',
  kilogram: 'issuedItems.units.kilogram',
  other: 'issuedItems.units.other',
}

/** Short "what goes here" description per unit (form hint / option title). */
export const ISSUED_ITEM_UNIT_DESCRIPTIONS: Record<IssuedItemUnit, string> = {
  piece: 'issuedItems.unitDescriptions.piece',
  pair: 'issuedItems.unitDescriptions.pair',
  set: 'issuedItems.unitDescriptions.set',
  box: 'issuedItems.unitDescriptions.box',
  pack: 'issuedItems.unitDescriptions.pack',
  roll: 'issuedItems.unitDescriptions.roll',
  meter: 'issuedItems.unitDescriptions.meter',
  liter: 'issuedItems.unitDescriptions.liter',
  kilogram: 'issuedItems.unitDescriptions.kilogram',
  other: 'issuedItems.unitDescriptions.other',
}

export function isIssuedItemUnit(value: unknown): value is IssuedItemUnit {
  return typeof value === 'string' && (ISSUED_ITEM_UNITS as readonly string[]).includes(value)
}

/**
 * Display normalisation: a stored code maps to itself, anything else (null, legacy free text
 * such as "Paar" from before the catalogue) renders as "Overige" instead of leaking raw data.
 */
export function normalizeIssuedItemUnit(value: string | null | undefined): IssuedItemUnit {
  return isIssuedItemUnit(value) ? value : 'other'
}

/**
 * Form normalisation: a template without a unit (new template, or a row from before the
 * catalogue) starts on the default; a known code is kept; unknown legacy text becomes "other"
 * so the user sees — and must confirm — that the old value no longer exists.
 */
export function issuedItemUnitForForm(value: string | null | undefined): IssuedItemUnit {
  if (value == null || value === '') return DEFAULT_ISSUED_ITEM_UNIT
  return normalizeIssuedItemUnit(value)
}

/** Translated unit label for any stored value (legacy values included). */
export function issuedItemUnitLabel(t: TranslateFn, value: string | null | undefined): string {
  return t(ISSUED_ITEM_UNIT_LABELS[normalizeIssuedItemUnit(value)])
}

/** "12 Paar" — quantity followed by the translated unit label. */
export function formatQuantityWithUnit(t: TranslateFn, quantity: number, value: string | null | undefined): string {
  return `${quantity} ${issuedItemUnitLabel(t, value)}`
}
