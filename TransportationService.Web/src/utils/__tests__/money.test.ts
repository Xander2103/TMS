import { describe, expect, it } from 'vitest'
import { lineTotal, roundMoney } from '../money'

describe('money (T-H4)', () => {
  it('multiplies exactly and rounds half away from zero to cents', () => {
    expect(lineTotal(2.5, 125.33)).toBe(313.33)
    expect(lineTotal(1.5, 0.01)).toBe(0.02)
    expect(lineTotal(3, 19.99)).toBe(59.97)
    expect(lineTotal(0.1, 0.7)).toBe(0.07)
    expect(lineTotal(1.005, 1)).toBe(1.01) // floating point would give 1.00
    expect(lineTotal(-2.5, 0.01)).toBe(-0.03)
    expect(lineTotal(12, 650)).toBe(7800)
  })

  it('rounds single amounts without float drift', () => {
    expect(roundMoney(313.325)).toBe(313.33)
    expect(roundMoney(1.005)).toBe(1.01)
    expect(roundMoney(650)).toBe(650)
  })
})
