import { describe, expect, it } from 'vitest'
import { ALERT_SEVERITY_META, ETA_SOURCE_LABELS, ETA_STATUS_META, LOCATION_SOURCE_LABELS, formatDelay } from '../types'

describe('formatDelay', () => {
  it('hides zero or unknown delays', () => {
    expect(formatDelay(null)).toBeNull()
    expect(formatDelay(0)).toBeNull()
    expect(formatDelay(-5)).toBeNull()
  })

  it('formats minutes and hours', () => {
    expect(formatDelay(45)).toBe('45 min')
    expect(formatDelay(60)).toBe('1 u')
    expect(formatDelay(80)).toBe('1 u 20 min')
  })
})

describe('operational meta maps', () => {
  it('labels every ETA source honestly (no fake live routing)', () => {
    expect(ETA_SOURCE_LABELS.Heuristic).toBe('raming')
    expect(ETA_SOURCE_LABELS.DispatcherOverride).toBe('handmatig')
  })

  it('covers every location source including Unavailable', () => {
    expect(Object.keys(LOCATION_SOURCE_LABELS)).toHaveLength(6)
    expect(LOCATION_SOURCE_LABELS.Unavailable).toBe('Geen locatie')
  })

  it('maps severities to escalating tones', () => {
    expect(ALERT_SEVERITY_META.Critical.tone).toBe('danger')
    expect(ETA_STATUS_META.Late.tone).toBe('danger')
  })
})
