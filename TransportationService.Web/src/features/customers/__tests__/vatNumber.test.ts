import { describe, expect, it } from 'vitest'
import { normalizeVatNumber, validateVatNumber } from '../utils/vatNumber'

describe('normalizeVatNumber', () => {
  it('strips separators and uppercases', () => {
    expect(normalizeVatNumber('be 0123.456-749')).toBe('BE0123456749')
  })

  it('returns null for empty input', () => {
    expect(normalizeVatNumber('')).toBeNull()
    expect(normalizeVatNumber('  .  ')).toBeNull()
  })
})

describe('validateVatNumber', () => {
  it('accepts valid Belgian numbers in any formatting', () => {
    // Safe test value: 01234567 % 97 = 48 → check digits 49 (not a real company).
    expect(validateVatNumber('BE0123456749')).toBeNull()
    expect(validateVatNumber('be 0123.456.749')).toBeNull()
  })

  it('rejects a wrong checksum with a specific message', () => {
    expect(validateVatNumber('BE0123456750')).toBe('Belgisch BTW-nummer heeft een ongeldig controlegetal.')
  })

  it('rejects wrong length and wrong leading digit', () => {
    expect(validateVatNumber('BE123456749')).toMatch(/BE \+ 10 cijfers/)
    expect(validateVatNumber('BE9123456749')).toMatch(/BE0 of BE1/)
  })

  it('is loose for foreign numbers', () => {
    expect(validateVatNumber('NL123456789B01')).toBeNull()
    expect(validateVatNumber('DE 129 273 398')).toBeNull()
    expect(validateVatNumber('A/B')).toBe('BTW-nummer heeft een ongeldig formaat.')
  })

  it('accepts empty input (VAT is optional)', () => {
    expect(validateVatNumber('')).toBeNull()
  })
})
