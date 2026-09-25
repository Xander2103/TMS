import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'
import { splitStreetLine } from '../addressFields'
import { postalCodeErrorKey } from '../postalCode'

describe('postalCodeErrorKey — format per country', () => {
  const valid: Array<[string, string]> = [
    ['BE', '3080'],
    ['BE', ' 1300 '],
    ['NL', '1234 AB'],
    ['NL', '1234AB'],
    ['NL', '1234 ab'],
    ['FR', '75008'],
    ['DE', '10115'],
    ['LU', '1234'],
    ['LU', 'L-1234'],
    // A country without a rule is never format-checked.
    ['IT', 'ANYTHING 99'],
    ['', '30800'],
    // Nothing typed = nothing to validate (the field stays optional).
    ['BE', ''],
    ['NL', '   '],
    // Lower-case country codes still find their rule.
    ['be', '9000'],
  ]
  it.each(valid)('%s "%s" is accepted', (country, code) => {
    expect(postalCodeErrorKey(country, code)).toBeNull()
  })

  const invalid: Array<[string, string, string]> = [
    ['BE', '30800', 'stopEditor.postalCode.BE'],
    ['BE', '308', 'stopEditor.postalCode.BE'],
    ['BE', '30A0', 'stopEditor.postalCode.BE'],
    ['be', '30800', 'stopEditor.postalCode.BE'],
    ['NL', '1234', 'stopEditor.postalCode.NL'],
    ['NL', '12345 AB', 'stopEditor.postalCode.NL'],
    ['NL', 'AB 1234', 'stopEditor.postalCode.NL'],
    ['FR', '7500', 'stopEditor.postalCode.FR'],
    ['FR', '750081', 'stopEditor.postalCode.FR'],
    ['DE', '1011', 'stopEditor.postalCode.DE'],
    ['LU', '12345', 'stopEditor.postalCode.LU'],
  ]
  it.each(invalid)('%s "%s" is rejected', (country, code, key) => {
    expect(postalCodeErrorKey(country, code)).toBe(key)
  })

  it('null/undefined country = no format check', () => {
    expect(postalCodeErrorKey(null, '30800')).toBeNull()
    expect(postalCodeErrorKey(undefined, '30800')).toBeNull()
  })

  it('the Belgian message reads as specified', () => {
    expect(translate('nl', postalCodeErrorKey('BE', '30800')!)).toBe('Een Belgische postcode heeft 4 cijfers.')
  })
})

describe('splitStreetLine — feeds the duplicate check only', () => {
  it.each([
    ['Avenue Sabin 1', 'Avenue Sabin', '1'],
    ['Noorderlaan 10A', 'Noorderlaan', '10A'],
    ['Kerkstraat 12 bus 3', 'Kerkstraat', '12 bus 3'],
    ['Rue du 11 Novembre 5', 'Rue du 11 Novembre', '5'],
  ])('"%s" → street "%s", number "%s"', (line, street, houseNumber) => {
    expect(splitStreetLine(line)).toEqual({ street, houseNumber })
  })

  it('a line without a number is all street; an empty line is nothing', () => {
    expect(splitStreetLine('Industrieterrein Noord')).toEqual({ street: 'Industrieterrein Noord', houseNumber: null })
    expect(splitStreetLine('   ')).toEqual({ street: null, houseNumber: null })
  })
})
