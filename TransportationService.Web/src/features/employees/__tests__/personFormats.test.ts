import { describe, expect, it } from 'vitest'
import { formatIban, formatNrn, normalizeIban, normalizeNrn, validateIban, validateNrn } from '../utils/personFormats'

describe('national register number', () => {
  it('normalises formatted and unformatted input to 11 digits, preserving leading zeroes', () => {
    expect(normalizeNrn('90.05.01-123.26')).toBe('90050112326')
    expect(normalizeNrn('90050112326')).toBe('90050112326')
    expect(normalizeNrn('05.02.03-004.38')).toBe('05020300438')
    expect(normalizeNrn('')).toBeNull()
  })

  it('formats 11 digits for display', () => {
    expect(formatNrn('90050112326')).toBe('90.05.01-123.26')
    expect(formatNrn('90.05.01-123.26')).toBe('90.05.01-123.26')
    // Not 11 digits — leave the user's input alone.
    expect(formatNrn('9005011')).toBe('9005011')
  })

  it('validates length and checksum with specific message keys', () => {
    expect(validateNrn('90.05.01-123.26')).toBeNull()
    expect(validateNrn('123')).toBe('employees.validation.nrnLength')
    expect(validateNrn('90050112399')).toBe('employees.validation.nrnChecksum')
    expect(validateNrn('')).toBeNull()
  })

  it('accepts post-2000 checksums (2-prefixed base)', () => {
    // 05 02 03 004 with post-2000 rule: 97 - ((2000000000 + 050203004) % 97)
    const body = 50203004
    const check = 97 - ((2000000000 + body) % 97)
    const digits = `050203004${String(check).padStart(2, '0')}`
    expect(validateNrn(digits)).toBeNull()
  })
})

describe('IBAN', () => {
  it('normalises spacing and casing', () => {
    expect(normalizeIban('be68 5390 0754 7034')).toBe('BE68539007547034')
    expect(normalizeIban('')).toBeNull()
  })

  it('validates via mod-97 with specific message keys', () => {
    expect(validateIban('BE68 5390 0754 7034')).toBeNull()
    expect(validateIban('NL91 ABNA 0417 1643 00')).toBeNull()
    expect(validateIban('BE68 5390 0754 7035')).toBe('employees.validation.ibanChecksum')
    expect(validateIban('B168')).toBe('employees.validation.ibanFormat')
    expect(validateIban('')).toBeNull()
  })

  it('formats valid IBANs in groups of four and leaves invalid input alone', () => {
    expect(formatIban('be68539007547034')).toBe('BE68 5390 0754 7034')
    expect(formatIban('BE68 5390 0754 7035')).toBe('BE68 5390 0754 7035')
  })
})
