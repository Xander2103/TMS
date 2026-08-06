import { describe, expect, it } from 'vitest'
import { addContractEndDate } from '../contractPresets'

describe('addContractEndDate', () => {
  it('a 1-month contract from the 1st ends the day before the same day next month', () => {
    expect(addContractEndDate('2026-01-01', 1)).toBe('2026-01-31')
  })

  it('a 12-month contract from Jan 1 ends Dec 31 the same year', () => {
    expect(addContractEndDate('2026-01-01', 12)).toBe('2026-12-31')
  })

  it('31 Jan + 1 month clamps to the end of February in a non-leap year', () => {
    expect(addContractEndDate('2026-01-31', 1)).toBe('2026-02-28')
  })

  it('31 Jan + 1 month clamps to 29 Feb in a leap year', () => {
    expect(addContractEndDate('2028-01-31', 1)).toBe('2028-02-29')
  })

  it('3-month and 6-month presets add the requested number of months minus one day', () => {
    expect(addContractEndDate('2026-03-15', 3)).toBe('2026-06-14')
    expect(addContractEndDate('2026-03-15', 6)).toBe('2026-09-14')
  })

  it('crosses a year boundary correctly', () => {
    expect(addContractEndDate('2026-12-01', 1)).toBe('2026-12-31')
    expect(addContractEndDate('2026-11-01', 3)).toBe('2027-01-31')
  })
})
