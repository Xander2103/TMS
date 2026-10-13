import { afterEach, describe, expect, it } from 'vitest'
import {
  formatCurrency, formatDecimal, formatQuantity, resetDecimalSeparatorPreference,
  setDecimalSeparatorPreference,
} from '../numbers'

afterEach(() => resetDecimalSeparatorPreference())

describe('formatQuantity — tenant decimal separator, no trailing zeros', () => {
  it('renders integers without a fraction part', () => {
    expect(formatQuantity(12)).toBe('12')
    expect(formatQuantity(0)).toBe('0')
  })

  it('renders fractions with the tenant comma and no padding', () => {
    expect(formatQuantity(12.5)).toBe('12,5')
    expect(formatQuantity(1234.25)).toBe('1.234,25')
  })

  it('rounds to the maximum fraction digits (default 3)', () => {
    expect(formatQuantity(1.23456)).toBe('1,235')
    expect(formatQuantity(1.23456, 1)).toBe('1,2')
    expect(formatQuantity(2.9999, 2)).toBe('3')
  })

  it('handles negatives and empties', () => {
    expect(formatQuantity(-1234.5)).toBe('-1.234,5')
    expect(formatQuantity(null)).toBe('')
    expect(formatQuantity(undefined)).toBe('')
    expect(formatQuantity(Number.NaN)).toBe('')
  })

  it('follows the tenant "." preference (mirror grouping)', () => {
    setDecimalSeparatorPreference('.')
    expect(formatQuantity(1234.25)).toBe('1,234.25')
    expect(formatQuantity(12)).toBe('12')
  })
})

describe('formatDecimal / formatCurrency', () => {
  it('pads to the requested digits and groups thousands', () => {
    expect(formatDecimal(1234.5)).toBe('1.234,50')
    // formatCurrency binds symbol and amount with a non-breaking space.
    const nbsp = ' '
    expect(formatCurrency(0)).toBe(`€${nbsp}0,00`)
    expect(formatCurrency(1234.56)).toBe(`€${nbsp}1.234,56`)
    expect(formatCurrency(null)).toBe('')
  })
})
