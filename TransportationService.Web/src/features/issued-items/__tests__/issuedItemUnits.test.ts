import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'
import {
  DEFAULT_ISSUED_ITEM_UNIT,
  formatQuantityWithUnit,
  ISSUED_ITEM_UNIT_DESCRIPTIONS,
  ISSUED_ITEM_UNIT_LABELS,
  ISSUED_ITEM_UNITS,
  isIssuedItemUnit,
  issuedItemUnitForForm,
  issuedItemUnitLabel,
  normalizeIssuedItemUnit,
} from '../issuedItemUnits'

const t = (key: string, params?: Record<string, string | number>) => translate('nl', key, params)

describe('issuedItemUnits', () => {
  it('exposes exactly the backend catalogue, in order, with piece as the default', () => {
    expect([...ISSUED_ITEM_UNITS]).toEqual(['piece', 'pair', 'set', 'box', 'pack', 'roll', 'meter', 'liter', 'kilogram', 'other'])
    expect(DEFAULT_ISSUED_ITEM_UNIT).toBe('piece')
  })

  it('recognises catalogue codes and rejects anything else', () => {
    for (const unit of ISSUED_ITEM_UNITS) expect(isIssuedItemUnit(unit)).toBe(true)
    expect(isIssuedItemUnit('Paar')).toBe(false)
    expect(isIssuedItemUnit('')).toBe(false)
    expect(isIssuedItemUnit(null)).toBe(false)
    expect(isIssuedItemUnit(undefined)).toBe(false)
    expect(isIssuedItemUnit(3)).toBe(false)
  })

  it('normalises legacy and empty values to "other" for display', () => {
    expect(normalizeIssuedItemUnit('pair')).toBe('pair')
    expect(normalizeIssuedItemUnit('Paar')).toBe('other')
    expect(normalizeIssuedItemUnit('stuks')).toBe('other')
    expect(normalizeIssuedItemUnit(null)).toBe('other')
    expect(normalizeIssuedItemUnit(undefined)).toBe('other')
  })

  it('starts a form on piece when nothing is stored, keeps a code, and flags legacy text as other', () => {
    expect(issuedItemUnitForForm(null)).toBe('piece')
    expect(issuedItemUnitForForm(undefined)).toBe('piece')
    expect(issuedItemUnitForForm('')).toBe('piece')
    expect(issuedItemUnitForForm('box')).toBe('box')
    expect(issuedItemUnitForForm('Doos')).toBe('other')
  })

  it('has a Dutch label and description for every unit', () => {
    const expectedLabels: Record<string, string> = {
      piece: 'Stuk', pair: 'Paar', set: 'Set', box: 'Doos', pack: 'Pak',
      roll: 'Rol', meter: 'Meter', liter: 'Liter', kilogram: 'Kilogram', other: 'Overige',
    }
    for (const unit of ISSUED_ITEM_UNITS) {
      expect(t(ISSUED_ITEM_UNIT_LABELS[unit])).toBe(expectedLabels[unit])
      // A missing key would echo the key itself.
      expect(t(ISSUED_ITEM_UNIT_DESCRIPTIONS[unit])).not.toBe(ISSUED_ITEM_UNIT_DESCRIPTIONS[unit])
      expect(t(ISSUED_ITEM_UNIT_DESCRIPTIONS[unit]).startsWith(`${expectedLabels[unit]} —`)).toBe(true)
    }
  })

  it('renders quantities with the translated label, also for legacy values', () => {
    expect(issuedItemUnitLabel(t, 'pair')).toBe('Paar')
    expect(formatQuantityWithUnit(t, 12, 'pair')).toBe('12 Paar')
    expect(formatQuantityWithUnit(t, 3, 'Paar')).toBe('3 Overige')
    expect(formatQuantityWithUnit(t, 0, null)).toBe('0 Overige')
  })
})
