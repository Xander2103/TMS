import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'
import {
  COST_PHASE_LABELS,
  COST_TYPE_LABELS,
  MANUAL_COST_TYPES,
  formatMarginPct,
  type TripCostType,
} from '../types'

describe('trip costing metadata', () => {
  it('labels every cost type and phase (table completeness)', () => {
    const allTypes = Object.keys(COST_TYPE_LABELS) as TripCostType[]
    expect(allTypes).toHaveLength(15)
    for (const type of allTypes) {
      // Key-map: de waarde is een vertaalsleutel die in het nl-bundle moet bestaan.
      expect(COST_TYPE_LABELS[type], `label ${type}`).toBe(`tripCosting.costType.${type}`)
      expect(translate('nl', COST_TYPE_LABELS[type]), `nl-vertaling ${type}`).not.toBe(COST_TYPE_LABELS[type])
    }
    expect(translate('nl', COST_PHASE_LABELS.Estimated)).toBe('Geschat')
    expect(translate('nl', COST_PHASE_LABELS.Actual)).toBe('Werkelijk')
  })

  it('offers only expense-style types for manual lines', () => {
    for (const type of MANUAL_COST_TYPES) {
      expect(COST_TYPE_LABELS[type]).toBeTruthy()
    }
    expect(MANUAL_COST_TYPES).not.toContain('DriverLabour')
    expect(MANUAL_COST_TYPES).toContain('Correction')
  })

  it('formats margins zero-safely', () => {
    expect(formatMarginPct(14.8)).toBe('14,8%')
    expect(formatMarginPct(0)).toBe('0,0%')
    expect(formatMarginPct(null)).toBe('—')
    expect(formatMarginPct(undefined)).toBe('—')
    expect(formatMarginPct(Number.NaN)).toBe('—')
  })
})
