import { describe, expect, it } from 'vitest'
import { parsePercent, validateSurchargeForm } from '../utils/billingConfig'

describe('parsePercent', () => {
  it('parses dot and comma decimals', () => {
    expect(parsePercent('3.5')).toBe(3.5)
    expect(parsePercent('3,5')).toBe(3.5)
    expect(parsePercent('0')).toBe(0)
  })

  it('returns null for empty or non-numeric input', () => {
    expect(parsePercent('')).toBeNull()
    expect(parsePercent('   ')).toBeNull()
    expect(parsePercent('abc')).toBeNull()
  })
})

describe('validateSurchargeForm', () => {
  it('accepts a valid percentage without dates', () => {
    expect(validateSurchargeForm({ percent: '3,5', effectiveFrom: '', effectiveUntil: '' })).toEqual({})
  })

  it('rejects an out-of-range or missing percentage', () => {
    expect(validateSurchargeForm({ percent: '', effectiveFrom: '', effectiveUntil: '' }).percent).toBeDefined()
    expect(validateSurchargeForm({ percent: '-1', effectiveFrom: '', effectiveUntil: '' }).percent).toBeDefined()
    expect(validateSurchargeForm({ percent: '101', effectiveFrom: '', effectiveUntil: '' }).percent).toBeDefined()
  })

  it('accepts the boundary values 0 and 100', () => {
    expect(validateSurchargeForm({ percent: '0', effectiveFrom: '', effectiveUntil: '' }).percent).toBeUndefined()
    expect(validateSurchargeForm({ percent: '100', effectiveFrom: '', effectiveUntil: '' }).percent).toBeUndefined()
  })

  it('flags an end date before the start date', () => {
    const errors = validateSurchargeForm({ percent: '3', effectiveFrom: '2026-02-01', effectiveUntil: '2026-01-01' })
    expect(errors.effectiveUntil).toBeDefined()
  })

  it('accepts an end date on or after the start date', () => {
    expect(
      validateSurchargeForm({ percent: '3', effectiveFrom: '2026-01-01', effectiveUntil: '2026-02-01' }).effectiveUntil,
    ).toBeUndefined()
  })
})
