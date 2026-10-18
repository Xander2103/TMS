import { describe, expect, it } from 'vitest'
import type { UnitTypeLabel } from '../api/unitTypesApi'
import { buildUnitTypesModel } from '../unitTypes'

/**
 * Master-data remediation 2026-10-18: pickers offer only what the backend accepts, displays
 * resolve every code ever used, and a value the order already carries never silently drops.
 */

const label = (code: string, name: string, overrides: Partial<UnitTypeLabel> = {}): UnitTypeLabel => ({
  code, name, isActive: true, allowForOrderEntry: true, allowForPricing: true, isDeleted: false, ...overrides,
})

const model = buildUnitTypesModel(
  [{ id: 'u-ep', code: 'EUROPALLET', name: 'Europallet' }],
  [
    label('EUROPALLET', 'Europallet'),
    label('TMKIST', 'Tijdelijke kist', { isActive: false }),
    label('UUR', 'Uur', { allowForOrderEntry: false }),
    label('OUD', 'Oude eenheid', { isDeleted: true, allowForPricing: true }),
  ],
  (name, l) => (!l || !l.isActive || l.isDeleted ? `${name} (inactief)` : `${name} (niet meer selecteerbaar)`),
)

describe('buildUnitTypesModel', () => {
  it('labels a retired code with its human name; only a truly unknown code falls back to the code', () => {
    expect(model.labelOf('TMKIST')).toBe('Tijdelijke kist')
    expect(model.labelOf('tmkist')).toBe('Tijdelijke kist')
    expect(model.labelOf('ONBEKEND')).toBe('ONBEKEND')
    expect(model.labelOf(null)).toBe('')
    expect(model.displayUnits.map((u) => u.code)).toEqual(['EUROPALLET', 'TMKIST', 'UUR', 'OUD'])
  })

  it('order pickers offer order-entry units and keep a selected unavailable code visible, marked', () => {
    expect(model.orderPickerOptions([]).map((u) => u.name)).toEqual(['Europallet'])
    const options = model.orderPickerOptions(['TMKIST', 'UUR', 'EUROPALLET', null, 'TMKIST'])
    expect(options.map((u) => [u.code, u.name])).toEqual([
      ['EUROPALLET', 'Europallet'],
      ['TMKIST', 'Tijdelijke kist (inactief)'],
      ['UUR', 'Uur (niet meer selecteerbaar)'],
    ])
    // A retained value never carries a real id (the price preview maps code → id).
    expect(options[1].id).not.toBe('u-ep')
    expect(model.orderOptions.some((o) => o.code === 'TMKIST')).toBe(false)
  })

  it('pricing pickers offer active, non-deleted pricing units', () => {
    expect(model.pricingPickerOptions([]).map((u) => u.code)).toEqual(['EUROPALLET', 'UUR'])
    expect(model.pricingPickerOptions(['OUD']).at(-1)).toMatchObject({ code: 'OUD', name: 'Oude eenheid (inactief)' })
  })
})
